using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DMT.Inventory;

internal static class ArchiveCompressorTests
{
    public static void Run(string temporaryRoot, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(temporaryRoot, "archive-compressor"); Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "source"); var archive = Path.Combine(source, "archiviati"); Directory.CreateDirectory(archive);
        var database = Path.Combine(folder, "zip.sqlite"); var store = new InventoryStore(database);
        byte[] Report(string id, string date) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = "1.0", snapshotId = id, collectedAt = date,
            device = new { status = "ok", data = new { systemUuid = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", hostname = "ZIP-TEST" } },
            maintenance = new { status = "error" }, padding = new string('a', 10000)
        });
        byte[] Put(string id, string date)
        {
            var bytes = Report(id, date); store.Import(bytes, "tenant", "site", id + ".json");
            File.WriteAllBytes(Path.Combine(archive, id + ".json"), bytes); return bytes;
        }
        var first = Put("september-one", "2026-09-16T12:00:00+02:00");
        var second = Put("september-two", "2026-09-30T20:00:00+02:00");
        Put("october", "2026-10-01T00:01:00+02:00");
        File.WriteAllText(Path.Combine(archive, "unrecognized.json"), "never remove this");
        var now = DateTimeOffset.Parse("2026-10-16T12:00:00+02:00");
        var result = ArchiveCompressor.CatchUp(database, source, "tenant", Path.Combine(folder, "initial.jsonl"), now);
        var month = result.Months.Single(); var zipPath = Path.Combine(archive, "202609-MTREPORT.zip");
        check(month is { Month: "202609", Status: "archived", Reports: 2, RemovedLoose: 2 }, "zip_closed_month_catchup");
        check(File.Exists(Path.Combine(archive, "october.json")), "zip_current_month_excluded");
        check(result.UnrecognizedFiles.Count == 1 && File.Exists(Path.Combine(archive, "unrecognized.json")), "zip_unknown_staged_preserved");
        check(!File.Exists(Path.Combine(archive, "september-one.json")) && !File.Exists(Path.Combine(archive, "september-two.json")), "zip_verified_loose_cleanup");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            check(zip.Entries.Count == 3, "zip_has_manifest_and_members");
            using var manifestInput = zip.GetEntry("DMT-MANIFEST.json")!.Open();
            var manifest = JsonSerializer.Deserialize<ArchiveZipManifest>(manifestInput)!;
            check(manifest.Reports.Count == 2 && manifest.Month == "202609" && manifest.Tenant == "tenant", "zip_manifest_contract");
            foreach (var (name, bytes) in new[] { ("september-one.json", first), ("september-two.json", second) })
            {
                using var input = zip.GetEntry(name)!.Open(); using var output = new MemoryStream(); input.CopyTo(output);
                check(output.ToArray().SequenceEqual(bytes), "zip_exact_original_bytes_" + name);
            }
        }
        check(new FileInfo(zipPath).Length < first.Length + second.Length, "zip_actual_compression");
        check(!Directory.EnumerateFiles(archive, "*.tmp").Any() && !File.Exists(Path.Combine(archive, ".dmt-archive.lock")), "zip_temporary_cleanup");
        var rerun = ArchiveCompressor.CatchUp(database, source, "tenant", Path.Combine(folder, "unused.jsonl"), now);
        check(rerun.Months.Count == 0 && !File.Exists(Path.Combine(folder, "unused.jsonl")), "zip_no_reacquisition_after_success");
        var originalZipHash = SHA256.HashData(File.ReadAllBytes(zipPath));
        Put("september-late", "2026-09-29T12:00:00+02:00");
        var late = ArchiveCompressor.CatchUp(database, source, "tenant", Path.Combine(folder, "late.jsonl"), now).Months.Single();
        check(late is { Status: "archived", Reports: 3, RemovedLoose: 1 }, "zip_late_arrival_safe_update");
        check(late.PreviousZip != null && SHA256.HashData(File.ReadAllBytes(late.PreviousZip)).SequenceEqual(originalZipHash), "zip_previous_archive_retained");
        var updatedHash = SHA256.HashData(File.ReadAllBytes(zipPath));
        File.WriteAllBytes(Path.Combine(archive, "september-one.json"), first);
        var duplicate = ArchiveCompressor.CatchUp(database, source, "tenant", Path.Combine(folder, "duplicate.jsonl"), now).Months.Single();
        check(duplicate is { Status: "archived", Reports: 3, RemovedLoose: 1 } && duplicate.PreviousZip == null, "zip_duplicate_loose_recovery");
        check(SHA256.HashData(File.ReadAllBytes(zipPath)).SequenceEqual(updatedHash), "zip_duplicate_does_not_rewrite_archive");
        Put("september-corrupt-test", "2026-09-28T12:00:00+02:00");
        File.WriteAllText(zipPath, "corrupt existing archive");
        var blocked = ArchiveCompressor.CatchUp(database, source, "tenant", Path.Combine(folder, "corrupt.jsonl"), now).Months.Single();
        check(blocked.Status == "blocked" && File.Exists(Path.Combine(archive, "september-corrupt-test.json")), "zip_corrupt_existing_preserves_source");
        check(File.ReadAllText(zipPath) == "corrupt existing archive", "zip_corrupt_archive_not_overwritten");
        check(!Directory.EnumerateFiles(archive, "*.tmp").Any(), "zip_failed_operation_temp_cleanup");
    }
}
