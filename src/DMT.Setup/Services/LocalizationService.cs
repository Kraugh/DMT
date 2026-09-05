using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, LanguagePackInfo> _packs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _fallbackStrings = new(StringComparer.Ordinal);
    private LanguagePackInfo? _selectedLanguage;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<LanguagePackInfo> VisibleLanguages { get; } = new();

    public LanguagePackInfo? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null || Equals(_selectedLanguage, value)) return;
            LoadLanguage(value.Code);
        }
    }

    public string this[string key]
        => _strings.TryGetValue(key, out var value)
            ? value
            : _fallbackStrings.TryGetValue(key, out var fallback)
                ? fallback
                : $"[MISSING: {key}]";

    public void Initialize()
    {
        var languagesPath = Path.Combine(AppContext.BaseDirectory, "languages");
        if (!Directory.Exists(languagesPath))
            throw new DirectoryNotFoundException($"Language directory not found: {languagesPath}");

        foreach (var file in Directory.EnumerateFiles(languagesPath, "*.json").OrderBy(p => p))
        {
            var info = ReadMetadata(file);
            if (info is null) continue;
            _packs[info.Code] = info;
        }

        if (!_packs.ContainsKey("en-US"))
            throw new InvalidOperationException("Canonical fallback language en-US is missing.");

        LoadStrings(_packs["en-US"].FilePath, _fallbackStrings);
        RefreshVisibleLanguages();

        var detected = ResolveSystemCulture(CultureInfo.InstalledUICulture);
        LoadLanguage(detected?.Code ?? "en-US");
    }

    public bool UnlockLanguage(string code, bool select = true)
    {
        if (!_packs.TryGetValue(code, out var pack) || !pack.Hidden)
            return false;

        if (!VisibleLanguages.Any(x => x.Code.Equals(pack.Code, StringComparison.OrdinalIgnoreCase)))
            VisibleLanguages.Add(pack);

        if (select) LoadLanguage(pack.Code);
        return true;
    }

    private void RefreshVisibleLanguages()
    {
        VisibleLanguages.Clear();
        foreach (var pack in _packs.Values.Where(p => !p.Hidden).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            VisibleLanguages.Add(pack);
    }

    private LanguagePackInfo? ResolveSystemCulture(CultureInfo culture)
    {
        if (_packs.TryGetValue(culture.Name, out var exact) && !exact.Hidden)
            return exact;

        var language = culture.TwoLetterISOLanguageName;
        return _packs.Values.FirstOrDefault(p => !p.Hidden && p.Code.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase));
    }

    private void LoadLanguage(string code)
    {
        if (!_packs.TryGetValue(code, out var pack)) pack = _packs["en-US"];

        _strings.Clear();
        LoadStrings(pack.FilePath, _strings);
        _selectedLanguage = pack;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguage)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static LanguagePackInfo? ReadMetadata(string file)
    {
        using var stream = File.OpenRead(file);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("_language", out var lang)) return null;

        var code = lang.GetProperty("code").GetString();
        var name = lang.GetProperty("name").GetString();
        var englishName = lang.GetProperty("englishName").GetString();
        var fallback = lang.TryGetProperty("fallback", out var f) && f.ValueKind != JsonValueKind.Null ? f.GetString() : null;
        var hidden = lang.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True;

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(englishName)) return null;
        return new LanguagePackInfo(code, name, englishName, fallback, hidden, file);
    }

    private static void LoadStrings(string file, Dictionary<string, string> target)
    {
        using var stream = File.OpenRead(file);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("strings", out var strings)) return;
        foreach (var prop in strings.EnumerateObject())
            target[prop.Name] = prop.Value.GetString() ?? string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
