using System.Text.Json;
using DMT.Inventory;

internal static class ArchiveMoverTests
{
    public static void Run(string temporaryRoot, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(temporaryRoot, "archive-mover"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source"); Directory.CreateDirectory(source);
        var archive = Path.Combine(source, "archiviati");
        var database = Path.Combine(folder, "move.sqlite"); var store = new InventoryStore(database);
        byte[] Report(string id) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0", snapshotId = id, collectedAt = "2026-09-16T12:00:00+02:00",
            device = new { status = "ok", data = new { systemUuid = "dddddddd-dddd-dddd-dddd-dddddddddddd", hostname = "MOVE-TEST" } },
            maintenance = new { status = "error" }
        });
        string Put(string name, byte[] bytes) { var path = Path.Combine(source, name + ".json"); File.WriteAllBytes(path, bytes); return path; }
        var bytes = Report("first"); store.Import(bytes, "tenant", "site", "first.json"); var first = Put("first", bytes);
        var created = DateTime.SpecifyKind(new DateTime(2026, 9, 16, 10, 0, 0), DateTimeKind.Utc);
        File.SetCreationTimeUtc(first, created); File.SetLastWriteTimeUtc(first, created.AddMinutes(1));
        var secondBytes = Report("second"); store.Import(secondBytes, "tenant", "site", "second.json"); var second = Put("second", secondBytes);
        var thirdBytes = Report("third"); store.Import(thirdBytes, "tenant", "site", "third.json"); var third = Put("third", thirdBytes);
        var unknown = Put("unknown", Report("unknown"));
        var plan = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-10-16T10:00:00Z"));
        File.WriteAllText(third, "changed since approval");
        var extra = Put("arrived-later", bytes);
        var result = ArchiveMover.Apply(database, plan, Path.Combine(folder, "first-run.jsonl"));
        check(result.Approved == 3 && result.Moved == 2 && result.Blocked == 1, "move_revalidates_allowlist");
        check(!File.Exists(first) && !File.Exists(second), "move_deletes_only_verified_originals");
        check(File.Exists(third) && File.Exists(unknown) && File.Exists(extra), "move_conflict_unknown_new_arrivals_preserved");
        check(File.ReadAllBytes(Path.Combine(archive, "first.json")).SequenceEqual(bytes), "move_destination_exact_bytes");
        check(File.GetCreationTimeUtc(Path.Combine(archive, "first.json")) == created, "move_preserves_creation_time");
        check(File.GetLastWriteTimeUtc(Path.Combine(archive, "first.json")) == created.AddMinutes(1), "move_preserves_modification_time");
        check(!File.Exists(Path.Combine(archive, ".dmt-archive.lock")), "move_lock_removed");
        check(!Directory.EnumerateFiles(archive, "*.tmp").Any(), "move_no_temporary_files_left");
        var journal = File.ReadAllLines(Path.Combine(folder, "first-run.jsonl"));
        check(journal.Any(line => line.Contains("copy_verified_before_delete")), "move_durable_journal");
        var rerun = ArchiveMover.Apply(database, plan, Path.Combine(folder, "second-run.jsonl"));
        check(rerun.Moved == 0 && rerun.AlreadyArchived == 2 && rerun.Blocked == 1, "move_restart_idempotency");
        File.WriteAllBytes(third, thirdBytes);
        var resumed = ArchiveMover.Apply(database, plan, Path.Combine(folder, "third-run.jsonl"), concurrency: 8);
        check(resumed.Moved == 1 && resumed.AlreadyArchived == 2 && resumed.Blocked == 0, "move_retry_blocked_without_overwrite");

        var collision = Put("collision", bytes);
        var collisionPlan = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-10-16T10:00:00Z"));
        File.WriteAllText(Path.Combine(archive, "collision.json"), "destination created after dry-run");
        var collisionResult = ArchiveMover.Apply(database, collisionPlan, Path.Combine(folder, "collision-run.jsonl"));
        check(collisionResult.Files.Single(f => f.Source == collision).Status == "blocked", "move_destination_race_blocks");
        check(File.Exists(collision) && File.ReadAllText(Path.Combine(archive, "collision.json")) == "destination created after dry-run", "move_collision_preserves_both");

        var locked = Put("locked", bytes);
        var lockPlan = ArchivePlanner.Simulate(database, source, "tenant", DateTimeOffset.Parse("2026-10-16T10:00:00Z"));
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var lockedResult = ArchiveMover.Apply(database, lockPlan, Path.Combine(folder, "locked-run.jsonl"));
            check(lockedResult.Files.Single(f => f.Source == locked).Status == "blocked", "move_source_in_use_preserved");
            check(File.Exists(locked), "move_locked_source_not_deleted");
        }
        var escaped = plan with { Files = plan.Files.Select(f => f.Path == first ? f with { Destination = Path.Combine(folder, "outside.json") } : f).ToArray() };
        try { ArchiveMover.Apply(database, escaped, Path.Combine(folder, "escape-run.jsonl")); throw new Exception("expected_invalid_destination"); }
        catch (InvalidDataException) { check(!File.Exists(Path.Combine(folder, "outside.json")), "move_rejects_path_escape"); }
    }
}
