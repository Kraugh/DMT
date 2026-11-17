using System.Security.Cryptography;

namespace DMT.Inventory;

public sealed record ConflictDisposition(string File, string Status, string? Reason, string? ReplacementId = null);

public static class ConflictCleanup
{
    public static IReadOnlyList<ConflictDisposition> Apply(string database, string source, string tenant,
        IReadOnlyList<ArchiveFilePlan> blocked, string journalPath, bool pruneSuperseded,
        CancellationToken cancellationToken = default)
    {
        source = Path.GetFullPath(source);
        var quarantine = Path.Combine(source, "conflitti");
        var archive = Path.Combine(source, "archiviati");
        ArchiveFiles.ValidateDirectory(source);
        Directory.CreateDirectory(quarantine); ArchiveFiles.ValidateDirectory(quarantine);
        using var gate = ArchiveFiles.LockDirectory(quarantine);
        using var journal = new ArchiveJournal(journalPath);
        using var evidence = new ArchiveEvidence(database, tenant);
        var catalog = evidence.Catalog().ToDictionary(e => e.SnapshotId);
        var systems = new InventoryStore(database).Systems(tenant);
        var candidates = blocked.Where(b => b.Origin == "source" && b.Action == "blocked")
            .Select(b => (Path: b.Path, Hash: b.Sha256)).ToList();
        candidates.AddRange(Directory.EnumerateFiles(quarantine, "*.json").Select(p => (p, (string?)null)));
        candidates.AddRange(Directory.EnumerateFiles(source, "*.json.tmp")
            .Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-1)).Select(p => (p, (string?)null)));
        var results = new List<ConflictDisposition>();
        foreach (var (path, expectedHash) in candidates.DistinctBy(c => Path.GetFullPath(c.Path), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConflictDisposition result;
            try
            {
                var full = Path.GetFullPath(path);
                var parent = Path.GetDirectoryName(full)!;
                if (!parent.Equals(source, StringComparison.OrdinalIgnoreCase) && !parent.Equals(quarantine, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("conflict_path_escape");
                using var original = ArchiveFiles.OpenForVerifiedDeletion(full);
                if (original.Length > 32 * 1024 * 1024) throw new InvalidDataException("file_too_large");
                using var buffer = new MemoryStream(); original.CopyTo(buffer);
                var bytes = buffer.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes));
                if (expectedHash != null && expectedHash != hash) throw new InvalidDataException("conflict_changed_since_plan");
                Snapshot? snapshot = null;
                try { snapshot = Snapshot.Parse(bytes); }
                catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { }
                // Never infer identity from filename/hostname, nor replace evidence under a reused ID.
                var matches = snapshot == null ? [] : systems.Where(s =>
                    (snapshot.Uuid != null && snapshot.Uuid == s["uuid"] as string || snapshot.Serial != null && snapshot.Serial == s["serial"] as string) &&
                    (snapshot.Uuid == null || s["uuid"] as string == snapshot.Uuid) &&
                    (snapshot.Serial == null || s["serial"] as string == snapshot.Serial)).ToArray();
                ArchiveEvidence.Entry? newer = null;
                if (pruneSuperseded && !full.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && snapshot != null && matches.Length == 1 &&
                    catalog.TryGetValue((string)matches[0]["snapshot_id"]!, out var entry) &&
                    DateTimeOffset.Parse(entry.CollectedAt) > DateTimeOffset.Parse(snapshot.CollectedAt)) newer = entry;
                if (newer != null && !File.Exists(Path.Combine(archive, Path.GetFileName(newer.FileName)))) newer = null;
                if (newer != null)
                {
                    evidence.Verify(newer.SnapshotId, newer.Hash);
                    if (Path.GetFileName(newer.FileName) != newer.FileName) throw new InvalidDataException("invalid_replacement_filename");
                    // Require the newer original in archiviati, held read-locked through retirement.
                    using var replacement = ArchiveFiles.OpenRead(Path.Combine(archive, newer.FileName));
                    ArchiveFiles.VerifyHash(replacement, newer.Hash);
                    journal.Write(new { phase = "superseded_before_delete", path = full, hash, snapshotId = snapshot!.Id,
                        replacementId = newer.SnapshotId, replacementHash = newer.Hash, newer.CollectedAt });
                    ArchiveFiles.DeleteLocked(original);
                    result = new(full, "pruned_superseded", null, newer.SnapshotId);
                }
                else if (parent.Equals(quarantine, StringComparison.OrdinalIgnoreCase)) result = new(full, "retained", "no_verified_newer_archive");
                else
                {
                    var destination = Path.Combine(quarantine, Path.GetFileName(full));
                    if (File.Exists(destination))
                    {
                        using var existing = ArchiveFiles.OpenRead(destination);
                        ArchiveFiles.VerifyHash(existing, hash);
                    }
                    else
                    {
                        using (var copy = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        { copy.Write(bytes); copy.Flush(true); ArchiveFiles.VerifyHash(copy, hash); }
                        File.SetCreationTimeUtc(destination, File.GetCreationTimeUtc(full));
                        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(full));
                    }
                    using var heldCopy = ArchiveFiles.OpenRead(destination); ArchiveFiles.VerifyHash(heldCopy, hash);
                    journal.Write(new { phase = "quarantine_verified_before_delete", path = full, destination, hash });
                    ArchiveFiles.DeleteLocked(original);
                    result = new(full, "quarantined", null);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                result = new(path, "blocked", e.Message);
                // Some SMB writers allow rename/delete but not our stable read lock.
                // Quarantine by same-volume atomic rename without reading/changing bytes;
                // this is explicitly NOT an acquired or verified report.
                if (e is IOException && e.Message == "source_open_failed" && expectedHash == null &&
                    Path.GetDirectoryName(Path.GetFullPath(path))!.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("reparse_point");
                        var destination = Path.Combine(quarantine, Path.GetFileName(path));
                        journal.Write(new { phase = "unreadable_before_atomic_rename", path, destination, reason = e.Message });
                        File.Move(path, destination, false);
                        result = new(path, "quarantined_unreadable", "not_acquired_writer_still_active");
                    }
                    catch (Exception moveError) when (moveError is IOException or InvalidDataException or UnauthorizedAccessException)
                    { result = new(path, "blocked", moveError.Message); }
                }
            }
            journal.Write(result); results.Add(result);
        }
        return results;
    }
}
