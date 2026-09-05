namespace DMT.Setup.Models;

public sealed record LanguagePackInfo(
    string Code,
    string Name,
    string EnglishName,
    string? Fallback,
    bool Hidden,
    string FilePath);
