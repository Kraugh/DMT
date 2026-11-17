using System.Security.Cryptography;
using System.Text.Json;
using DMT.Inventory;
using Microsoft.Data.Sqlite;

internal static class ArchivePlannerTests
{
    public static void Run(string temporaryRoot, Action<bool, string> check)
    {
        var folder = Path.Combine(temporaryRoot, "archive-planner"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source"); Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "archiviati");
        var database = Path.Combine(folder, "plan.sqlite");
        var store = new InventoryStore(database);
        byte[] Report(string id, string date) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0", snapshotId = id, collectedAt = date,
            device = new { status = "ok", data = new { systemUuid = "cccccccc-cccc-cccc-cccc-cccccccccccc", hostname = "ARCHIVE-TEST" } },
            maintenance = new { status = "error" }
        });
        var september = Report("september", "2026-09-15T12:00:00+02:00");
        // UTC September 30 is already October 1 in Rome.
        var october = Report("october", "2026-09-30T23:30:00Z");
        var unknown = Report("unknown", "2026-09-15T10:00:00Z");
        store.Import(september, "tenant", "site", "september.json");
        store.Import(october, "tenant", "site", "october.json");
        store.Import(unknown, "other-tenant", "site", "unknown.json");
        File.WriteAllBytes(Path.Combine(source, "september.json"), september);
        File.WriteAllBytes(Path.Combine(source, "october.json"), october);
        File.WriteAllBytes(Path.Combine(source, "unknown.json"), unknown);
        File.WriteAllText(Path.Combine(source, "malformed.json"), "{");
        File.WriteAllText(Path.Combine(source, "ignore.tmp"), "not-json");
        var original = Directory.GetFiles(source).ToDictionary(p => p, File.ReadAllBytes);
        var initialDate = DateTimeOffset.Parse("2026-10-16T15:00:00+02:00");
        var plan = ArchivePlanner.Simulate(database, source, "tenant", initialDate);
        check(plan.DryRun && plan.Mode == "simulation", "archive_simulation_only");
        check(!Directory.Exists(archive), "archive_no_directory_creation");
        check(plan.Files.Count == 4, "archive_ignores_tmp");
        check(plan.Files.Count(f => f.Action == "would_move_after_copy_verification") == 2, "archive_only_acquired");
        check(plan.Files.Single(f => f.SnapshotId == "unknown").Reason == "not_imported", "archive_tenant_isolation");
        check(plan.Files.Single(f => f.Path.EndsWith("malformed.json")).Reason == "invalid_json", "archive_invalid_json");
        check(plan.Months.Single(m => m.Month == "202609").Action == "would_compress_and_verify", "archive_missed_month_catchup");
        check(plan.Months.Single(m => m.Month == "202610").Action == "scheduled", "archive_open_month_waits");
        check(plan.Months.Single(m => m.Month == "202610").DueAtRome == "2026-10-31T23:59:00.0000000+01:00", "archive_rome_dst_due");
        check(plan.Files.Single(f => f.SnapshotId == "october").Month == "202610", "archive_rome_collection_month");
        check(original.All(f => File.ReadAllBytes(f.Key).SequenceEqual(f.Value)), "archive_no_source_writes");
        check(store.Attempts("tenant").Count == 0, "archive_no_import_side_effects");
        var boundary = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-10-31T23:59:00+01:00"));
        check(boundary.Months.All(m => m.Due), "archive_month_end_boundary");
        var beforeBoundary = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-10-31T23:58:59+01:00"));
        check(!beforeBoundary.Months.Single(m => m.Month == "202610").Due, "archive_before_month_end_boundary");

        Directory.CreateDirectory(archive);
        File.WriteAllBytes(Path.Combine(archive, "september.json"), september);
        File.WriteAllText(Path.Combine(archive, "october.json"), "different bytes");
        var missed = Report("missed", "2026-09-29T13:00:00+02:00");
        store.Import(missed, "tenant", "site", "missed.json");
        File.WriteAllBytes(Path.Combine(archive, "missed.json"), missed);
        var zip = Path.Combine(archive, "202609-MTREPORT.zip"); File.WriteAllText(zip, "never overwrite this archive");
        var zipHash = SHA256.HashData(File.ReadAllBytes(zip));
        var reconciled = ArchivePlanner.Simulate(database, source, "tenant", initialDate);
        check(reconciled.Files.Single(f => f.Origin == "source" && f.SnapshotId == "september").Action == "would_remove_source_after_staged_copy_verification", "archive_matching_destination");
        check(reconciled.Files.Single(f => f.Origin == "source" && f.SnapshotId == "october").Reason == "destination_conflict", "archive_destination_conflict");
        check(reconciled.Files.Single(f => f.SnapshotId == "missed").Action == "already_staged", "archive_reconcile_staged");
        check(reconciled.Months.Single().Reports == 2, "archive_deduplicate_matching_loose_copy");
        check(reconciled.Months.Single().Reason == "archive_exists_review_required", "archive_existing_zip_blocks");
        check(SHA256.HashData(File.ReadAllBytes(zip)).SequenceEqual(zipHash), "archive_existing_zip_unchanged");

        File.WriteAllBytes(Path.Combine(source, "content-conflict.json"), Report("september", "2026-09-15T14:00:00+02:00"));
        var conflicting = ArchivePlanner.Simulate(database, source, "tenant", initialDate);
        check(conflicting.Files.Single(f => f.Path.EndsWith("content-conflict.json")).Reason == "snapshot_content_conflict", "archive_changed_snapshot_blocked");
        var future = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        check(future.Files.Any(f => f.Reason == "future_collection"), "archive_future_collection_blocked");

        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString()))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE snapshots SET raw_json=X'7B7D' WHERE tenant='tenant' AND id='september'";
            command.ExecuteNonQuery();
        }
        var corrupt = ArchivePlanner.Simulate(database, source, "tenant", initialDate);
        check(corrupt.Files.Where(f => f.SnapshotId == "september").All(f => f.Reason == "database_evidence_corrupt"), "archive_validates_database_raw_bytes");

        var unsafeSource = Path.Combine(folder, "unsafe"); Directory.CreateDirectory(unsafeSource);
        File.WriteAllBytes(Path.Combine(unsafeSource, "october.json"), october);
        File.WriteAllText(Path.Combine(unsafeSource, "archiviati"), "a file instead of a directory");
        var unsafePlan = ArchivePlanner.Simulate(database, unsafeSource, "tenant", initialDate);
        check(unsafePlan.Issues.Contains("unsafe_archive_directory"), "archive_unsafe_destination_reported");
        check(unsafePlan.Files.Single().Action == "blocked" && unsafePlan.Months.Count == 0, "archive_unsafe_destination_fails_closed");
        var missingDatabase = Path.Combine(folder, "missing.sqlite");
        try { ArchivePlanner.Simulate(missingDatabase, source, "tenant", initialDate); throw new Exception("expected_readonly_open_failure"); }
        catch (SqliteException) { check(!File.Exists(missingDatabase), "archive_missing_database_not_created"); }
    }
}
