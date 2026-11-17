using System.Text.Json;

namespace DMT.Inventory;

public sealed class LanguageCatalog
{
    private readonly Dictionary<string, Pack> packs = new(StringComparer.OrdinalIgnoreCase);
    public LanguageCatalog(string folder)
    {
        foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var metadata = document.RootElement.GetProperty("_language");
            var code = metadata.GetProperty("code").GetString()!;
            packs.Add(code, new(code, metadata.GetProperty("name").GetString()!,
                metadata.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean(),
                JsonSerializer.Deserialize<Dictionary<string, string>>(document.RootElement.GetProperty("strings"))!));
        }
        if (!packs.ContainsKey("en-US")) throw new InvalidDataException("missing_fallback_language");
    }
    public object Visible => packs.Values.Where(p => !p.Hidden).OrderBy(p => p.Name).Select(p => new { p.Code, p.Name });
    public object Get(string code)
    {
        var pack = packs.GetValueOrDefault(code) ?? packs["en-US"];
        var strings = new Dictionary<string, string>(packs["en-US"].Strings);
        foreach (var pair in pack.Strings) strings[pair.Key] = pair.Value;
        return new { pack.Code, pack.Name, Strings = strings };
    }
    private sealed record Pack(string Code, string Name, bool Hidden, Dictionary<string, string> Strings);
}
