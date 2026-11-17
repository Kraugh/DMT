using System.Diagnostics;
using System.Text.Json;

namespace DMT.Inventory;

public sealed record ImportSummary(int Found, int Imported, int Duplicates, int Rejected, long ElapsedMs);
public static class FolderImporter
{
    public static ImportSummary Import(InventoryStore store, string folder, string tenant, string site,
        Action<int, long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(site)) throw new ArgumentException("tenant_and_site_required");
        var timer = Stopwatch.StartNew();
        int found = 0, imported = 0, duplicates = 0, rejected = 0;
        // Enumerate this configured directory only: avoid recycle bins and unrelated trees.
        foreach (var path in Directory.EnumerateFiles(folder).Where(p => Path.GetExtension(p).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            found++;
            string result; string? code = null;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("file_too_large");
                using var buffer = new MemoryStream(); stream.CopyTo(buffer);
                result = store.Import(buffer.ToArray(), tenant, site, Path.GetFileName(path));
                if (result == "imported") imported++; else duplicates++;
            }
            catch (Exception error) when (error is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                result = "rejected"; rejected++;
                code = error switch { InvalidDataException invalid => invalid.Message, JsonException => "invalid_json", UnauthorizedAccessException => "access_denied", _ => "read_failed" };
            }
            store.RecordAttempt(tenant, site, Path.GetFileName(path), result, code);
            progress?.Invoke(found, timer.ElapsedMilliseconds);
        }
        return new(found, imported, duplicates, rejected, timer.ElapsedMilliseconds);
    }
}
