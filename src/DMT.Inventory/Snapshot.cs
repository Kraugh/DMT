using System.Globalization;
using System.Text.Json;

namespace DMT.Inventory;

public sealed record SoftwareItem(string Name, string? Publisher, string? Version, string Source);
public sealed record Snapshot(string Id, string CollectedAt, string? Uuid, string? Serial,
    string Manufacturer, string Hostname, string? OsName, string? OsVersion, string? OsBuild,
    IReadOnlyList<SoftwareItem> Software, string? OsStatus, string? SoftwareStatus,
    string? MaintenanceStatus, string? CollectionStatus, string SectionsJson)
{
    public static Snapshot Parse(byte[] bytes)
    {
        var input = bytes.AsMemory();
        if (bytes.AsSpan().StartsWith(new byte[] { 239, 187, 191 })) input = input[3..];
        using var document = JsonDocument.Parse(input);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invalid_root");
        ValidateUniqueProperties(root);
        if (Text(root, "schemaVersion") != "1.0") throw new InvalidDataException("unsupported_schema");
        var id = Required(root, "snapshotId");
        var collectedAt = Required(root, "collectedAt");
        if (!(collectedAt.EndsWith('Z') || collectedAt.Length >= 6 &&
            collectedAt[^3] == ':' && collectedAt[^6] is '+' or '-') ||
            !DateTimeOffset.TryParseExact(collectedAt,
            new[] { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new InvalidDataException("invalid_collected_at");
        var device = Data(root, "device");
        var uuid = Identifier(Text(device, "systemUuid"));
        if (uuid != null && (!Guid.TryParse(uuid, out var guid) || guid == Guid.Empty || guid == Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"))) uuid = null;
        if (uuid != null) uuid = Guid.Parse(uuid).ToString("D").ToUpperInvariant();
        var serial = Identifier(Text(device, "biosSerialNumber"));
        var manufacturer = Text(device, "manufacturer")?.Trim() ?? "";
        if (manufacturer.Length == 0) serial = null;
        if (uuid == null && serial == null) throw new InvalidDataException("missing_reliable_identity");
        var os = Data(root, "os");
        var software = Data(root, "software");
        var items = new List<SoftwareItem>();
        foreach (var source in new[] { "win32", "appx" })
        {
            if (software.ValueKind == JsonValueKind.Undefined || !software.TryGetProperty(source, out var list) || list.ValueKind == JsonValueKind.Null) continue;
            if (list.ValueKind != JsonValueKind.Array) throw new InvalidDataException("invalid_software_list");
            foreach (var item in list.EnumerateArray())
                items.Add(new(Required(item, "name"), Text(item, "publisher"), Text(item, "version"), source));
        }
        var sections = new Dictionary<string, JsonElement>();
        foreach (var property in root.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object && property.Value.TryGetProperty("status", out _))
                sections[property.Name] = property.Value.Clone();
        return new(id, date.ToUniversalTime().ToString("O"), uuid, serial, manufacturer,
            Required(device, "hostname"), Text(os, "name"), Text(os, "displayVersion") ?? Text(os, "version"),
            Text(os, "build"), items, Status(root, "os"), Status(root, "software"),
            Status(root, "maintenance"), Status(root, "collection"), JsonSerializer.Serialize(sections));
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("duplicate_json_property");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateUniqueProperties(item);
    }

    private static string? Identifier(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized is null or "" or "UNKNOWN" or "NONE" or "N/A" or "DEFAULT STRING" or "SYSTEM SERIAL NUMBER" or "TO BE FILLED BY O.E.M." or "0" ? null : normalized;
    }
    private static string Required(JsonElement element, string key) =>
        Text(element, key) is { } value && !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new InvalidDataException("missing_" + key);
    private static string? Status(JsonElement root, string key) => root.TryGetProperty(key, out var section) ? Text(section, "status") : null;
    private static JsonElement Data(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var section)) return default;
        if (section.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invalid_" + key);
        if (!section.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null) return default;
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invalid_" + key + "_data");
        return data;
    }
    private static string? Text(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return null;
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("invalid_object");
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => throw new InvalidDataException("invalid_" + key) };
    }
}
