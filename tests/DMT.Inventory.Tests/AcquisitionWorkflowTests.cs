using System.Text.Json;
using DMT.Inventory;

static class AcquisitionWorkflowTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "workflow"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source"); Directory.CreateDirectory(source);
        var database = Path.Combine(folder, "inventory.sqlite");
        var journals = Path.Combine(folder, "journals");
        var now = DateTimeOffset.UtcNow;
        byte[] Report(string id, string name) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0", snapshotId = id, collectedAt = now.ToString("o"),
            device = new { status = "ok", data = new { systemUuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", biosSerialNumber = "TEST-001", hostname = name } }
        });
        var valid = Report("workflow-snapshot", "TEST-PC");
        var conflicting = Report("workflow-snapshot", "CONFLICT-PC");
        new InventoryStore(database).Import(valid, "test", "site", "valid.json");
        File.WriteAllBytes(Path.Combine(source, "valid.json"), valid);
        File.WriteAllBytes(Path.Combine(source, "conflict.json"), conflicting);
        var result = AcquisitionWorkflow.Run(database, source, "test", "site", journals);
        check(result.Import.Duplicates == 1 && result.Import.Rejected == 1, "workflow_duplicate_conflict_counts");
        check(result.Archive?.Moved == 1, "workflow_moves_only_verified_payload");
        check(File.ReadAllBytes(Path.Combine(source, "archiviati", "valid.json")).SequenceEqual(valid), "workflow_archive_exact_bytes");
        check(File.ReadAllBytes(Path.Combine(source, "conflict.json")).SequenceEqual(conflicting), "workflow_preserves_conflict");
        check(File.Exists(Path.Combine(result.EvidenceDirectory, "plan.json")) && File.Exists(Path.Combine(result.EvidenceDirectory, "result.json")), "workflow_evidence_saved");
        var retry = AcquisitionWorkflow.Run(database, source, "test", "site", journals);
        check(retry.Import.Imported == 0 && retry.Import.Rejected == 1 && retry.Archive?.Moved == 0, "workflow_incremental_retry");
        using var gate = new FileStream(database + ".acquire.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var rejected = false;
        try { AcquisitionWorkflow.Run(database, source, "test", "site", journals); }
        catch (IOException) { rejected = true; }
        check(rejected, "workflow_excludes_concurrent_acquisition");
    }
}
