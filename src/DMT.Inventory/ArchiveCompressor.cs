using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DMT.Inventory;

public sealed record ArchiveZipMember(string Name, string SnapshotId, string Sha256, long Bytes,
    string CollectedAt, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc);
public sealed record ArchiveZipManifest(string Format, string Tenant, string Month, string CreatedAtRome, IReadOnlyList<ArchiveZipMember> Reports);
public sealed record ArchiveCompressionResult(string Month, string ZipPath, string Status, int Reports, int RemovedLoose,
    long ZipBytes, string? Reason, string? PreviousZip);
public sealed record ArchiveCompressionSummary(long ElapsedMs, IReadOnlyList<ArchiveCompressionResult> Months, IReadOnlyList<string> UnrecognizedFiles);

public static class ArchiveCompressor
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private const string ManifestName = "DMT-MANIFEST.json";
    private sealed record Held(string Path, FileStream Stream, ArchiveZipMember Member);

    public static ArchiveCompressionSummary CatchUp(string database, string source, string tenant, string journalPath,
        DateTimeOffset now, Action<ArchiveCompressionResult, long>? progress = null, CancellationToken cancellationToken = default)
    {
        var timer = Stopwatch.StartNew(); var results = new List<ArchiveCompressionResult>(); var unknown = new List<string>();
        var archive = Path.Combine(Path.GetFullPath(source), "archiviati");
        try { ArchiveFiles.ValidateDirectory(archive); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return new(0, results, unknown); }
        using var evidence = new ArchiveEvidence(database, tenant);
        // Lightweight relational metadata filters current-month files without rereading their JSON.
        var catalog = evidence.Catalog().GroupBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        var currentMonth = TimeZoneInfo.ConvertTime(now, Rome).ToString("yyyyMM", CultureInfo.InvariantCulture);
        var candidates = new List<(string Path, ArchiveEvidence.Entry Entry, string Month)>();
        foreach (var file in Directory.EnumerateFiles(archive).Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!catalog.TryGetValue(Path.GetFileName(file), out var entry)) { unknown.Add(file); continue; }
            var month = TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(entry.CollectedAt, CultureInfo.InvariantCulture), Rome).ToString("yyyyMM", CultureInfo.InvariantCulture);
            // First useful opportunity after expiry: never include the month still in progress.
            if (string.CompareOrdinal(month, currentMonth) < 0) candidates.Add((file, entry, month));
        }
        if (candidates.Count == 0) return new(timer.ElapsedMilliseconds, results, unknown);
        using var archiveLock = ArchiveFiles.LockDirectory(archive);
        using var journal = new ArchiveJournal(journalPath);
        foreach (var group in candidates.GroupBy(c => c.Month).OrderBy(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var zipPath = Path.Combine(archive, group.Key + "-MTREPORT.zip");
            var temporary = zipPath + ".dmt-" + Guid.NewGuid().ToString("N") + ".tmp";
            var backup = zipPath + ".dmt-previous-" + Guid.NewGuid().ToString("N") + ".zip";
            var held = new List<Held>(); FileStream? previousStream = null; ZipArchive? previousZip = null;
            var temporaryOwned = false; string? retainedBackup = null;
            ArchiveCompressionResult result;
            try
            {
                var members = new Dictionary<string, ArchiveZipMember>(StringComparer.OrdinalIgnoreCase);
                string? previousHash = null;
                if (File.Exists(zipPath))
                {
                    previousStream = ArchiveFiles.OpenRead(zipPath);
                    previousHash = Convert.ToHexString(SHA256.HashData(previousStream)); previousStream.Position = 0;
                    previousZip = new ZipArchive(previousStream, ZipArchiveMode.Read, true);
                    var previous = VerifyZip(previousZip, tenant, group.Key, evidence);
                    foreach (var member in previous.Reports) members.Add(member.Name, member);
                }
                foreach (var candidate in group)
                {
                    var stream = ArchiveFiles.OpenForVerifiedDeletion(candidate.Path);
                    try
                    {
                        evidence.Verify(candidate.Entry.SnapshotId, candidate.Entry.Hash);
                        ArchiveFiles.VerifyHash(stream, candidate.Entry.Hash);
                        var member = new ArchiveZipMember(Path.GetFileName(candidate.Path), candidate.Entry.SnapshotId, candidate.Entry.Hash,
                            stream.Length, candidate.Entry.CollectedAt, File.GetCreationTimeUtc(candidate.Path), File.GetLastWriteTimeUtc(candidate.Path));
                        if (member.Name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("reserved_manifest_name");
                        if (members.TryGetValue(member.Name, out var existing) && (existing.Sha256 != member.Sha256 || existing.SnapshotId != member.SnapshotId))
                            throw new InvalidDataException("zip_member_conflict");
                        held.Add(new(candidate.Path, stream, member));
                    }
                    catch { stream.Dispose(); throw; }
                }
                var additions = held.Where(h => !members.ContainsKey(h.Member.Name)).ToArray();
                foreach (var added in additions) members.Add(added.Member.Name, added.Member);
                var manifest = new ArchiveZipManifest("dmt-mtreport-v1", tenant, group.Key, TimeZoneInfo.ConvertTime(now, Rome).ToString("O"),
                    members.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray());
                if (previousZip == null || additions.Length > 0)
                {
                    journal.Write(new { phase = "zip_started", zipPath, temporary, backup = previousZip != null ? backup : null, month = group.Key });
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        temporaryOwned = true;
                        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                        {
                            if (previousZip != null)
                                foreach (var entry in previousZip.Entries.Where(e => !e.FullName.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)))
                                { using var input = entry.Open(); using var target = zip.CreateEntry(entry.FullName, CompressionLevel.Optimal).Open(); input.CopyTo(target); }
                            foreach (var added in additions)
                            { added.Stream.Position = 0; using var target = zip.CreateEntry(added.Member.Name, CompressionLevel.Optimal).Open(); added.Stream.CopyTo(target); }
                            using var manifestStream = zip.CreateEntry(ManifestName, CompressionLevel.Optimal).Open();
                            JsonSerializer.Serialize(manifestStream, manifest);
                        }
                        output.Flush(true);
                    }
                    string newHash;
                    using (var check = ArchiveFiles.OpenRead(temporary))
                    {
                        newHash = Convert.ToHexString(SHA256.HashData(check)); check.Position = 0;
                        using var zip = new ZipArchive(check, ZipArchiveMode.Read, true); VerifyZip(zip, tenant, group.Key, evidence);
                    }
                    previousZip?.Dispose(); previousZip = null; previousStream?.Dispose(); previousStream = null;
                    if (previousHash == null) File.Move(temporary, zipPath, false);
                    else { File.Replace(temporary, zipPath, backup); retainedBackup = backup; }
                    temporaryOwned = false;
                    using var published = ArchiveFiles.OpenRead(zipPath); ArchiveFiles.VerifyHash(published, newHash);
                    if (retainedBackup != null)
                    { using var previous = ArchiveFiles.OpenRead(retainedBackup); ArchiveFiles.VerifyHash(previous, previousHash!); }
                }
                previousZip?.Dispose(); previousZip = null; previousStream?.Dispose(); previousStream = null;
                // Keep the canonical ZIP locked against changes while retiring loose verified members.
                using var finalStream = ArchiveFiles.OpenRead(zipPath);
                using var finalZip = new ZipArchive(finalStream, ZipArchiveMode.Read, true);
                var verified = VerifyZip(finalZip, tenant, group.Key, evidence);
                var finalMembers = verified.Reports.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
                if (finalMembers.Count != manifest.Reports.Count || manifest.Reports.Any(m => !finalMembers.TryGetValue(m.Name, out var member) || member.Sha256 != m.Sha256))
                    throw new InvalidDataException("published_zip_manifest_changed");
                foreach (var item in held)
                    if (!finalMembers.TryGetValue(item.Member.Name, out var member) || member.Sha256 != item.Member.Sha256)
                        throw new InvalidDataException("published_zip_missing_member");
                journal.Write(new { phase = "published_zip_verified", zipPath, reports = verified.Reports.Count, retainedBackup });
                var removed = 0; string? deleteFailure = null;
                foreach (var item in held)
                {
                    try
                    {
                        journal.Write(new { phase = "zip_member_verified_before_delete", item.Path, zipPath, item.Member.SnapshotId, item.Member.Sha256 });
                        ArchiveFiles.DeleteLocked(item.Stream); item.Stream.Dispose(); removed++;
                    }
                    catch (IOException error) { deleteFailure = error.Message; }
                }
                result = new(group.Key, zipPath, deleteFailure == null ? "archived" : "loose_cleanup_incomplete", verified.Reports.Count, removed, finalStream.Length, deleteFailure, retainedBackup);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                result = new(group.Key, zipPath, "blocked", 0, 0, 0, error.Message, retainedBackup);
            }
            finally
            {
                previousZip?.Dispose(); previousStream?.Dispose();
                foreach (var item in held) item.Stream.Dispose();
                if (temporaryOwned) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            journal.Write(result); results.Add(result); progress?.Invoke(result, timer.ElapsedMilliseconds);
        }
        return new(timer.ElapsedMilliseconds, results, unknown);
    }

    private static ArchiveZipManifest VerifyZip(ZipArchive zip, string tenant, string month, ArchiveEvidence evidence)
    {
        var manifestEntry = zip.GetEntry(ManifestName) ?? throw new InvalidDataException("zip_manifest_missing");
        if (manifestEntry.Length > 16 * 1024 * 1024) throw new InvalidDataException("zip_manifest_too_large");
        using var metadata = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<ArchiveZipManifest>(metadata) ?? throw new InvalidDataException("zip_manifest_invalid");
        if (manifest.Format != "dmt-mtreport-v1" || manifest.Tenant != tenant || manifest.Month != month || manifest.Reports == null || zip.Entries.Count != manifest.Reports.Count + 1)
            throw new InvalidDataException("zip_manifest_mismatch");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in manifest.Reports)
        {
            if (string.IsNullOrWhiteSpace(member.Name) || member.Name != Path.GetFileName(member.Name) || member.Name.Contains('/') || member.Name.Contains('\\') ||
                !member.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !names.Add(member.Name) || member.Bytes is < 0 or > 32 * 1024 * 1024 ||
                member.Name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(member.SnapshotId) || member.Sha256 is not { Length: 64 }) throw new InvalidDataException("zip_member_invalid");
            if (!DateTimeOffset.TryParse(member.CollectedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var collected) ||
                TimeZoneInfo.ConvertTime(collected, Rome).ToString("yyyyMM", CultureInfo.InvariantCulture) != month)
                throw new InvalidDataException("zip_member_wrong_month");
            if (evidence.Verify(member.SnapshotId, member.Sha256) != member.CollectedAt) throw new InvalidDataException("zip_member_date_mismatch");
            var entry = zip.GetEntry(member.Name) ?? throw new InvalidDataException("zip_member_missing");
            if (entry.Length != member.Bytes) throw new InvalidDataException("zip_member_length_mismatch");
            using var input = entry.Open();
            if (Convert.ToHexString(SHA256.HashData(input)) != member.Sha256) throw new InvalidDataException("zip_member_hash_mismatch");
        }
        if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new InvalidDataException("zip_duplicate_entries");
        return manifest;
    }
}
