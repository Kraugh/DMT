using System.Text.Json;

namespace DMT.Inventory;

public sealed record AcquisitionResult(ImportSummary Import, ArchiveMoveSummary? Archive,
    ArchiveCompressionSummary? Compression, string EvidenceDirectory, IReadOnlyList<string> Issues,
    IReadOnlyList<ConflictDisposition>? Conflicts = null);

/// <summary>One explicit operator action: acquire, verify, archive eligible originals, catch up closed months.</summary>
public static class AcquisitionWorkflow
{
    public static AcquisitionResult Run(string database, string source, string tenant, string site,
        string journalDirectory, Action<string, int>? progress = null, CancellationToken cancellationToken = default,
        bool reconcileConflicts = false, bool pruneSupersededConflicts = false)
    {
        database = Path.GetFullPath(database);
        source = Path.GetFullPath(source);
        journalDirectory = Path.GetFullPath(journalDirectory);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("source_unavailable");
        Directory.CreateDirectory(journalDirectory);
        // Also excludes acquisition from a second management window/process for this database.
        using var gate = new FileStream(database + ".acquire.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var runDirectory = Path.Combine(journalDirectory, "acquire-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);
        var issues = new List<string>();
        var imported = FolderImporter.Import(new InventoryStore(database), source, tenant, site,
            (count, _) => progress?.Invoke("import", count), cancellationToken);
        progress?.Invoke("verify", 0);
        var plan = ArchivePlanner.Simulate(database, source, tenant, DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
        WriteEvidence(Path.Combine(runDirectory, "plan.json"), plan);
        ArchiveMoveSummary? archived = null;
        ArchiveCompressionSummary? compressed = null;
        IReadOnlyList<ConflictDisposition>? conflicts = null;
        if (plan.Issues.Count == 0)
        {
            progress?.Invoke("archive", 0);
            archived = ArchiveMover.Apply(database, plan, Path.Combine(runDirectory, "move.jsonl"),
                (_, _) => progress?.Invoke("archive", 0), cancellationToken);
            WriteEvidence(Path.Combine(runDirectory, "move-summary.json"), archived);
            if (reconcileConflicts)
            {
                progress?.Invoke("conflicts", 0);
                conflicts = ConflictCleanup.Apply(database, source, tenant, plan.Files,
                    Path.Combine(runDirectory, "conflicts.jsonl"), pruneSupersededConflicts, cancellationToken);
            }
            progress?.Invoke("compress", 0);
            compressed = ArchiveCompressor.CatchUp(database, source, tenant, Path.Combine(runDirectory, "compress.jsonl"),
                DateTimeOffset.UtcNow, cancellationToken: cancellationToken);
        }
        else issues.AddRange(plan.Issues);
        // Blocked/conflicting originals never enter the approved move allowlist.
        if (conflicts == null) issues.AddRange(plan.Files.Where(f => f.Origin == "source" && f.Action == "blocked").Select(f => f.Reason ?? "blocked_source"));
        else issues.AddRange(conflicts.Where(f => f.Status == "blocked").Select(f => f.Reason ?? "blocked_conflict"));
        var result = new AcquisitionResult(imported, archived, compressed, runDirectory, issues, conflicts);
        WriteEvidence(Path.Combine(runDirectory, "result.json"), result);
        return result;
    }

    private static void WriteEvidence<T>(string path, T value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
        stream.Flush(true);
    }
}
