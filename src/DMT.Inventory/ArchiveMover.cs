using System.Diagnostics;

namespace DMT.Inventory;

public sealed record ArchiveMoveResult(string Source, string Destination, string? SnapshotId, string Status, string? Reason);
public sealed record ArchiveMoveSummary(int Approved, int Moved, int AlreadyArchived, int Blocked, long ElapsedMs, string Journal, IReadOnlyList<ArchiveMoveResult> Files);

public static class ArchiveMover
{
    public static ArchiveMoveSummary Apply(string database, ArchiveSimulation approved, string journalPath,
        Action<ArchiveMoveResult, long>? progress = null, CancellationToken cancellationToken = default, int concurrency = 4)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("verified_archive_requires_windows");
        if (concurrency is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(concurrency));
        if (!approved.DryRun || approved.Mode != "simulation" || approved.Issues.Count != 0) throw new InvalidDataException("invalid_approved_plan");
        var source = Path.GetFullPath(approved.Source);
        var archive = Path.Combine(source, "archiviati");
        if (!Path.GetFullPath(approved.ArchiveDirectory).Equals(archive, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("invalid_archive_path");
        var items = approved.Files.Where(f => f.Origin == "source" && f.Action is "would_move_after_copy_verification" or "would_remove_source_after_staged_copy_verification").ToArray();
        foreach (var item in items)
            if (item.SnapshotId == null || item.Sha256 is not { Length: 64 } ||
                !Path.GetDirectoryName(Path.GetFullPath(item.Path))!.TrimEnd(Path.DirectorySeparatorChar).Equals(source.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(item.Destination).Equals(Path.Combine(archive, Path.GetFileName(item.Path)), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(item.Path).Equals(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("invalid_approved_file_path");
        if (items.Select(i => Path.GetFullPath(i.Destination)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
            throw new InvalidDataException("duplicate_approved_destination");
        var timer = Stopwatch.StartNew();
        var results = new List<ArchiveMoveResult>();
        using var evidence = new ArchiveEvidence(database, approved.Tenant);
        using var journal = new ArchiveJournal(journalPath);
        journal.Write(new { phase = "begin", source, archive, approved = items.Length, tenant = approved.Tenant });
        ArchiveFiles.ValidateDirectory(source);
        Directory.CreateDirectory(archive); ArchiveFiles.ValidateDirectory(archive);
        using var archiveLock = ArchiveFiles.LockDirectory(archive);
        Parallel.ForEach(items, new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken }, item =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveMoveResult result;
            try
            {
                using var currentEvidence = new ArchiveEvidence(database, approved.Tenant);
                currentEvidence.Verify(item.SnapshotId!, item.Sha256!);
                FileStream? original = null;
                try { original = ArchiveFiles.OpenForVerifiedDeletion(item.Path); }
                catch (FileNotFoundException)
                {
                    using var existing = ArchiveFiles.OpenRead(item.Destination);
                    ArchiveFiles.VerifyHash(existing, item.Sha256!);
                }
                if (original == null) result = new(item.Path, item.Destination, item.SnapshotId, "already_archived", null);
                else
                {
                    using (original)
                    {
                        ArchiveFiles.VerifyHash(original, item.Sha256!);
                        if (original.Length != item.Bytes) throw new InvalidDataException("file_length_changed");
                        var creation = File.GetCreationTimeUtc(item.Path);
                        var modified = File.GetLastWriteTimeUtc(item.Path);
                        var temporary = item.Destination + ".dmt-" + Guid.NewGuid().ToString("N") + ".tmp";
                        var createdTemporary = false;
                        try
                        {
                            if (!File.Exists(item.Destination))
                            {
                                journal.Write(new { phase = "copy_started", item.Path, item.Destination, temporary, item.SnapshotId, item.Sha256 });
                                using (var copy = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                                {
                                    createdTemporary = true;
                                    original.CopyTo(copy); copy.Flush(true); ArchiveFiles.VerifyHash(copy, item.Sha256!);
                                }
                                File.SetCreationTimeUtc(temporary, creation); File.SetLastWriteTimeUtc(temporary, modified);
                                File.Move(temporary, item.Destination, false); createdTemporary = false;
                            }
                            using var destination = ArchiveFiles.OpenRead(item.Destination);
                            ArchiveFiles.VerifyHash(destination, item.Sha256!);
                            // Both handles exclude writes/replacement until this explicit delete-by-handle.
                            journal.Write(new { phase = "copy_verified_before_delete", item.Path, item.Destination, item.SnapshotId, item.Sha256, creation, modified });
                            ArchiveFiles.DeleteLocked(original);
                        }
                        finally
                        {
                            if (createdTemporary)
                            {
                                // Only this invocation's exact, recorded, create-new temporary path.
                                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                            }
                        }
                    }
                    result = new(item.Path, item.Destination, item.SnapshotId, "moved", null);
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                result = new(item.Path, item.Destination, item.SnapshotId, "blocked", error is InvalidDataException ? error.Message : "file_operation_failed: " + error.Message);
            }
            journal.Write(result);
            lock (results) { results.Add(result); progress?.Invoke(result, timer.ElapsedMilliseconds); }
        });
        var summary = new ArchiveMoveSummary(items.Length, results.Count(r => r.Status == "moved"), results.Count(r => r.Status == "already_archived"),
            results.Count(r => r.Status == "blocked"), timer.ElapsedMilliseconds, Path.GetFullPath(journalPath), results);
        journal.Write(new { phase = "complete", summary.Approved, summary.Moved, summary.AlreadyArchived, summary.Blocked, summary.ElapsedMs });
        return summary;
    }
}
