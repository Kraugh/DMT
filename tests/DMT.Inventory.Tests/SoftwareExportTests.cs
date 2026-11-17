using DMT.Inventory;

static class SoftwareExportTests
{
    public static void Run(Action<bool, string> check)
    {
        Dictionary<string, object?> Row(string name, string? version) => new() { ["hostname"] = name, ["version"] = version };
        var rows = new[] { Row("PC-B", "11.31.1"), Row("PC-A", "11.28.2"), Row("pc-a", "11.28.2"), Row("PC-A", "11.30.1") };
        check(SoftwareExports.Computers(rows) == "PC-A\r\nPC-B\r\n", "export_txt_unique_sorted_computers");
        var csv = SoftwareExports.ComputerVersions(rows);
        check(csv == "hostname,version\r\n\"PC-A\",\"11.28.2\"\r\n\"PC-A\",\"11.30.1\"\r\n\"PC-B\",\"11.31.1\"\r\n", "export_versions_distinct_pairs");
        check(SoftwareExports.Computers([]) == "" && SoftwareExports.ComputerVersions([]) == "hostname,version\r\n", "export_empty_results");
        check(SoftwareExports.CsvCell("=SUM(1,2)") == "\"'=SUM(1,2)\"", "export_formula_protection");
        check(SoftwareExports.CsvCell("a,\"b\"\nc") == "\"a,\"\"b\"\"\nc\"", "export_csv_escaping");
        check(SoftwareExports.ComputerVersions([Row("PC-A", null)]).Contains("\"PC-A\",\"\""), "export_unknown_version_preserved");
        check(SoftwareExports.Computers(Enumerable.Range(0, 125).Select(i => Row($"PC-{i:000}", "1"))).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 125, "export_all_pages");
        check(SoftwareExports.Computers([Row("PC-A\r\nPC-B", "1")]) == "PC-A  PC-B\r\n", "export_txt_line_injection");
    }
}
