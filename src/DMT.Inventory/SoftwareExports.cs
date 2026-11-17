using System.Text;

namespace DMT.Inventory;

public static class SoftwareExports
{
    // TXT is a set of computer names, independent of matching versions/install sources.
    public static string Computers(IEnumerable<Dictionary<string, object?>> rows) =>
        string.Concat(rows.Select(row => row["hostname"]?.ToString() ?? "")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => name.Replace("\r", " ").Replace("\n", " ") + "\r\n"));

    // One row per distinct computer/version pair; full product metadata remains in the full CSV.
    public static string ComputerVersions(IEnumerable<Dictionary<string, object?>> rows)
    {
        var pairs = rows.Select(row => (Hostname: row["hostname"]?.ToString() ?? "", Version: row["version"]?.ToString() ?? ""))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Hostname))
            .DistinctBy(pair => (pair.Hostname.ToUpperInvariant(), pair.Version))
            .OrderBy(pair => pair.Hostname, StringComparer.OrdinalIgnoreCase).ThenBy(pair => pair.Version, StringComparer.Ordinal);
        var output = new StringBuilder("hostname,version\r\n");
        foreach (var pair in pairs) output.Append(CsvCell(pair.Hostname)).Append(',').Append(CsvCell(pair.Version)).Append("\r\n");
        return output.ToString();
    }

    public static string CsvCell(object? value)
    {
        var text = value?.ToString() ?? "";
        if (text.TrimStart().StartsWith('=') || text.TrimStart().StartsWith('+') || text.TrimStart().StartsWith('-') || text.TrimStart().StartsWith('@')) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
