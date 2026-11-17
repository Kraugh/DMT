using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DMT.Inventory;

public sealed record ArchiveFilePlan(string Origin, string Path, string Destination, long Bytes,
    string? SnapshotId, string? Month, string? Sha256, string Action, string? Reason);
public sealed record ArchiveMonthPlan(string Month, string ZipPath, string DueAtRome, bool Due,
    int Reports, long Bytes, string Action, string? Reason, IReadOnlyList<string> Members);
public sealed record ArchiveSimulation(string Mode, bool DryRun, string GeneratedAtRome, string Tenant,
    string Source, string ArchiveDirectory, IReadOnlyList<ArchiveFilePlan> Files,
    IReadOnlyList<ArchiveMonthPlan> Months, IReadOnlyList<string> Issues, long ElapsedMs);

/// <summary>Read-only planning. This type contains no write, move, ZIP creation or delete operation.</summary>
public static class ArchivePlanner
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
    private sealed record Evidence(string Hash, string CollectedAt, bool Valid);

    public static ArchiveSimulation Simulate(string database, string source, string tenant,
        DateTimeOffset now, Action<int, long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("tenant_required");
        var timer = Stopwatch.StartNew();
        source = System.IO.Path.GetFullPath(source);
        var archive = System.IO.Path.Combine(source, "archiviati");
        var localNow = TimeZoneInfo.ConvertTime(now, Rome);
        var files = new List<ArchiveFilePlan>();
        var issues = new List<string>();
        var archiveFiles = new Dictionary<string, ArchiveFilePlan>(StringComparer.OrdinalIgnoreCase);
        var archiveNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var archiveScanComplete = true;
        var archiveObserved = false;
        var processed = 0;

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = System.IO.Path.GetFullPath(database), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using (var version = connection.CreateCommand())
        {
            version.CommandText = "PRAGMA query_only=ON; PRAGMA user_version;";
            if (Convert.ToInt32(version.ExecuteScalar()) != 1) throw new InvalidDataException("unsupported_database_version");
        }
        using var transaction = connection.BeginTransaction(deferred: true);
        var evidence = new Dictionary<string, Evidence?>(StringComparer.Ordinal);

        Evidence? FindEvidence(string id)
        {
            if (evidence.TryGetValue(id, out var cached)) return cached;
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT sha256,collected_at,raw_json FROM snapshots WHERE tenant=$tenant AND id=$id";
            command.Parameters.AddWithValue("$tenant", tenant); command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            Evidence? result = null;
            if (reader.Read())
            {
                var hash = reader.GetString(0);
                result = new(hash, reader.GetString(1), hash == Convert.ToHexString(SHA256.HashData((byte[])reader.GetValue(2))));
            }
            evidence[id] = result;
            return result;
        }

        ArchiveFilePlan Inspect(string path, string origin)
        {
            var destination = System.IO.Path.Combine(archive, System.IO.Path.GetFileName(path));
            long length = 0; string? id = null, month = null, hash = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("reparse_point");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                length = stream.Length;
                if (length > 32 * 1024 * 1024) throw new InvalidDataException("file_too_large");
                using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var snapshot = Snapshot.Parse(bytes);
                id = snapshot.Id;
                var collected = DateTimeOffset.Parse(snapshot.CollectedAt, CultureInfo.InvariantCulture);
                month = TimeZoneInfo.ConvertTime(collected, Rome).ToString("yyyyMM", CultureInfo.InvariantCulture);
                hash = Convert.ToHexString(SHA256.HashData(bytes));
                var acquired = FindEvidence(id);
                if (acquired == null) throw new InvalidDataException("not_imported");
                if (!acquired.Valid) throw new InvalidDataException("database_evidence_corrupt");
                if (acquired.Hash != hash || acquired.CollectedAt != snapshot.CollectedAt)
                    throw new InvalidDataException("snapshot_content_conflict");
                if (collected > now) throw new InvalidDataException("future_collection");
                return new(origin, path, destination, length, id, month, hash,
                    origin == "source" ? "would_move_after_copy_verification" : "already_staged", null);
            }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                var reason = error switch { InvalidDataException invalid => invalid.Message,
                    JsonException => "invalid_json", UnauthorizedAccessException => "access_denied", _ => "read_failed" };
                return new(origin, path, destination, length, id, month, hash, "blocked", reason);
            }
            finally { progress?.Invoke(++processed, timer.ElapsedMilliseconds); }
        }

        // GetAttributes distinguishes an absent archive directory from an inaccessible one.
        try
        {
            var attributes = File.GetAttributes(archive);
            archiveObserved = true;
            if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("unsafe_archive_directory");
            foreach (var path in Directory.EnumerateFiles(archive).Order(StringComparer.OrdinalIgnoreCase))
            {
                archiveNames.Add(System.IO.Path.GetFileName(path));
                if (System.IO.Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) zipNames.Add(System.IO.Path.GetFileName(path));
                if (!System.IO.Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                var item = Inspect(path, "archive");
                if (!archiveFiles.TryAdd(System.IO.Path.GetFileName(path), item))
                    throw new InvalidDataException("archive_name_collision");
                files.Add(item);
            }
            // A directory named like a report must not be mistaken for a free target filename.
            foreach (var path in Directory.EnumerateDirectories(archive)) archiveNames.Add(System.IO.Path.GetFileName(path));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            if (archiveObserved || error is not DirectoryNotFoundException && error is not FileNotFoundException)
            {
                archiveScanComplete = false;
                issues.Add(error is InvalidDataException invalid ? invalid.Message : "archive_scan_failed");
            }
        }

        foreach (var path in Directory.EnumerateFiles(source).Where(p => System.IO.Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
        {
            var item = Inspect(path, "source");
            var name = System.IO.Path.GetFileName(path);
            if (item.Action != "blocked")
            {
                if (!archiveScanComplete) item = item with { Action = "blocked", Reason = "archive_scan_incomplete" };
                else if (archiveFiles.TryGetValue(name, out var staged))
                    item = staged.Action != "blocked" && staged.Sha256 == item.Sha256
                        ? item with { Action = "would_remove_source_after_staged_copy_verification" }
                        : item with { Action = "blocked", Reason = "destination_conflict" };
                else if (archiveNames.Contains(name)) item = item with { Action = "blocked", Reason = "destination_conflict" };
            }
            files.Add(item);
        }

        // Fail closed if distinct source paths collapse to the same Windows destination name.
        var collisions = files.Where(f => f.Origin == "source").GroupBy(f => f.Destination, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Count; i++)
            if (collisions.Contains(files[i].Destination)) files[i] = files[i] with { Action = "blocked", Reason = "destination_name_collision" };

        var months = new List<ArchiveMonthPlan>();
        foreach (var group in files.Where(f => f.Action != "blocked").GroupBy(f => f.Month).OrderBy(g => g.Key))
        {
            var month = group.Key!;
            var first = DateTime.ParseExact(month + "01", "yyyyMMdd", CultureInfo.InvariantCulture);
            var dueLocal = DateTime.SpecifyKind(first.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59), DateTimeKind.Unspecified);
            var dueAt = new DateTimeOffset(dueLocal, Rome.GetUtcOffset(dueLocal));
            var due = now >= dueAt;
            var zipName = month + "-MTREPORT.zip";
            var members = group.GroupBy(f => f.Destination, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
            var reason = !archiveScanComplete ? "archive_scan_incomplete" : zipNames.Contains(zipName) || archiveNames.Contains(zipName) ? "archive_exists_review_required" : null;
            months.Add(new(month, System.IO.Path.Combine(archive, zipName), dueAt.ToString("O"), due,
                members.Length, members.Sum(f => f.Bytes), reason != null ? "blocked" : due ? "would_compress_and_verify" : "scheduled",
                reason, members.Select(f => f.Destination).Order(StringComparer.OrdinalIgnoreCase).ToArray()));
        }
        return new("simulation", true, localNow.ToString("O"), tenant, source, archive, files, months, issues, timer.ElapsedMilliseconds);
    }
}
