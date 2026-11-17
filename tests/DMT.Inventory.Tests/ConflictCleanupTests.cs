using System.Text.Json;
using DMT.Inventory;

static class ConflictCleanupTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "conflict-cleanup"); Directory.CreateDirectory(folder);
        byte[] Report(string id, DateTimeOffset at, string name = "TEST-PC", string uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", string serial = "TEST-001") => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0", snapshotId = id, collectedAt = at.ToString("o"),
            device = new { status = "ok", data = new { systemUuid = uuid, biosSerialNumber = serial, hostname = name } }
        });
        var now = DateTimeOffset.UtcNow;
        var source = Path.Combine(folder, "source"); Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "archiviati"); Directory.CreateDirectory(archive);
        var db = Path.Combine(folder, "test.sqlite"); var store = new InventoryStore(db);
        var original = Report("old", now.AddDays(-3));
        store.Import(original, "test", "site", "old.json");
        File.WriteAllBytes(Path.Combine(source, "old.json"), Report("old", now.AddDays(-3), "CHANGED-NAME"));
        var latest = Report("new", now);
        File.WriteAllBytes(Path.Combine(source, "new.json"), latest);
        var temp = Path.Combine(source, "partial.json.tmp"); File.WriteAllText(temp, "unfinished"); File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddDays(-3));
        // A leftover filename is recoverable; active sharing protection remains authoritative.
        File.WriteAllText(Path.Combine(archive, ".dmt-archive.lock"), "stale");
        var result = AcquisitionWorkflow.Run(db, source, "test", "site", Path.Combine(folder, "journals"),
            reconcileConflicts: true, pruneSupersededConflicts: true);
        check(result.Import.Imported == 1 && result.Import.Rejected == 1, "cleanup_acquires_latest_first");
        check(result.Conflicts!.Count(c => c.Status == "pruned_superseded") == 1, "cleanup_prunes_old_conflict_after_new_archive");
        check(File.ReadAllBytes(Path.Combine(archive, "new.json")).SequenceEqual(latest), "cleanup_newest_original_exact_archived");
        check(store.Import(original, "test", "site", "old.json") == "duplicate", "cleanup_preserves_old_database_evidence");
        check(!File.Exists(Path.Combine(source, "old.json")) && Directory.GetFiles(source).Length == 0, "cleanup_source_root_empty");
        check(File.ReadAllText(Path.Combine(source, "conflitti", "partial.json.tmp")) == "unfinished", "cleanup_preserves_unfinished_temp");
        check(result.Conflicts!.Count(c => c.Status == "quarantined") == 1, "cleanup_temp_quarantined_not_deleted");
        // A conflicting newest observation has no trusted later replacement: retain it separately.
        var alteredLatest = Report("new", now, "LATEST-VARIANT");
        File.WriteAllBytes(Path.Combine(source, "new.json"), alteredLatest);
        var retry = AcquisitionWorkflow.Run(db, source, "test", "site", Path.Combine(folder, "journals"),
            reconcileConflicts: true, pruneSupersededConflicts: true);
        check(retry.Conflicts!.Any(c => c.Status == "quarantined"), "cleanup_retains_conflict_without_newer_observation");
        check(File.ReadAllBytes(Path.Combine(source, "conflitti", "new.json")).SequenceEqual(alteredLatest), "cleanup_quarantine_exact_bytes");
        // Permission to prune is explicit. A default run keeps even an obsolete conflict.
        File.WriteAllBytes(Path.Combine(source, "old.json"), Report("old", now.AddDays(-3), "ANOTHER-VARIANT"));
        var retained = AcquisitionWorkflow.Run(db, source, "test", "site", Path.Combine(folder, "journals"), reconcileConflicts: true);
        check(retained.Conflicts!.Any(c => c.Status == "quarantined" && Path.GetFileName(c.File) == "old.json"), "cleanup_pruning_requires_explicit_option");
        check(File.Exists(Path.Combine(source, "conflitti", "old.json")), "cleanup_default_retains_old_variant");
        var lockedPath = Path.Combine(source, "held.json");
        var heldBytes = Report("unreadable", now.AddMinutes(1));
        File.WriteAllBytes(lockedPath, heldBytes);
        using (var writer = new FileStream(lockedPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        {
            var lockedRun = AcquisitionWorkflow.Run(db, source, "test", "site", Path.Combine(folder, "journals"), reconcileConflicts: true);
            check(lockedRun.Import.Imported == 0 && lockedRun.Conflicts!.Any(c => c.Status == "quarantined_unreadable"), "cleanup_unreadable_renamed_without_claiming_import");
        }
        check(File.ReadAllBytes(Path.Combine(source, "conflitti", "held.json")).SequenceEqual(heldBytes), "cleanup_unreadable_atomic_rename_preserves_bytes");
        // An actively held archive lock must still block a second writer.
        using var held = new FileStream(Path.Combine(archive, ".dmt-archive.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        var rejected = false;
        try { AcquisitionWorkflow.Run(db, source, "test", "site", Path.Combine(folder, "journals")); }
        catch (IOException) { rejected = true; }
        check(rejected, "cleanup_active_archive_lock_not_stolen");
    }
}
