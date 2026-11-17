using System.Text;
using System.Text.Json;
using DMT.Inventory;
using Microsoft.Data.Sqlite;

var folder = Path.Combine(Path.GetTempPath(), "DMT-inventory-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
Console.WriteLine("temporary_directory=" + folder);
var assertions = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); assertions++; }
void Reject(Action action, string code)
{
    try { action(); throw new Exception("expected_" + code); }
    catch (InvalidDataException e) { Check(e.Message == code, code); }
}
byte[] Report(string id, string date, string hostname = "TEST-PC", string uuid = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", string serial = "TEST-001", string software = "Tesar System") =>
    JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = "1.0", snapshotId = id, collectedAt = date,
        device = new { status = "ok", data = new { systemUuid = uuid, biosSerialNumber = serial, manufacturer = "Test Vendor", hostname } },
        os = new { status = "ok", data = new { name = "Microsoft Windows 10 Pro", displayVersion = "22H2", build = 19045 } },
        software = new { status = "ok", data = new { win32 = new[] { new { name = software, publisher = "Test Publisher", version = "2.0" } }, appx = Array.Empty<object>() } },
        maintenance = new { status = "error", errors = new[] { new { code = "TEST_FAILURE" } } },
        collection = new { status = "partial" }
    });
try
{
    var database = Path.Combine(folder, "test.sqlite");
    var store = new InventoryStore(database);
    var newer = Report("new", "2026-10-14T14:00:00+02:00", "RENAMED-PC", software: "Motis2");
    Check(store.Import(newer, "one", "site", "new.json") == "imported", "import");
    Check(store.Import(newer, "one", "site", "new.json") == "duplicate", "idempotency");
    var older = Report("old", "2026-10-14T11:00:00Z");
    store.Import(older, "one", "site", "old.json");
    var system = store.Systems("one", name: "Windows 10").Single();
    var asset = (string)system["asset_id"]!;
    Check((string)system["hostname"]! == "RENAMED-PC", "late_arrival");
    Check((string)system["maintenance_status"]! == "error", "maintenance_separate");
    Check(store.History("one", asset).Count == 2, "history");
    Check(store.Software("one", name: "Motis", publisher: "Publisher", version: "2.0", asset: asset).Count == 1, "software_filters");
    Check(store.Software("one", name: "Tesar").Count == 0, "old_software_not_current");
    Check(store.Products("one").Count == 1, "products");
    Check(store.Systems("two").Count == 0, "tenant_isolation");
    Check(store.Systems("one", site: "other").Count == 0, "site_isolation");
    Check(store.Software("one", name: "' OR 1=1 --").Count == 0, "parameterized_query");
    Check(store.Import(newer, "two", "site", "new.json") == "imported", "tenant_dedup_scope");
    Reject(() => store.Import(Report("new", "2026-10-14T15:00:00Z"), "one", "site", "collision.json"), "snapshot_id_conflict");
    Reject(() => store.Import(Report("conflict", "2026-10-15T00:00:00Z", uuid: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "one", "site", "conflict.json"), "identity_conflict");
    Check(store.History("one", asset).Count == 2, "conflict_rollback");
    Reject(() => store.Import(Report("bad-id", "2026-10-15T00:00:00Z", uuid: "", serial: "To Be Filled By O.E.M."), "one", "site", "bad.json"), "missing_reliable_identity");
    Reject(() => Snapshot.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":\"2.0\"}")), "unsupported_schema");
    Reject(() => Snapshot.Parse(Report("bad-time", "yesterday")), "invalid_collected_at");
    Reject(() => Snapshot.Parse(Report("no-zone", "2026-10-14T12:00:00")), "invalid_collected_at");
    Reject(() => Snapshot.Parse(Encoding.UTF8.GetBytes("[]")), "invalid_root");
    Reject(() => Snapshot.Parse(Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"2.0\"}")), "duplicate_json_property");
    // A serial-only snapshot can correlate with the previously observed UUID + serial.
    store.Import(Report("serial", "2026-10-13T00:00:00Z", uuid: ""), "one", "site", "serial.json");
    Check(store.Systems("one").Count == 1, "serial_fallback");
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database }.ToString()))
    {
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT raw_json FROM snapshots WHERE tenant='one' AND id='new'";
        Check(((byte[])command.ExecuteScalar()!).SequenceEqual(newer), "exact_raw_bytes");
        command.CommandText = "PRAGMA integrity_check";
        Check((string)command.ExecuteScalar()! == "ok", "integrity");
        command.CommandText = "PRAGMA foreign_key_check";
        Check(command.ExecuteScalar() == null, "foreign_keys");
    }
    var incoming = Path.Combine(folder, "incoming"); Directory.CreateDirectory(incoming);
    File.WriteAllBytes(Path.Combine(incoming, "a.json"), newer);
    File.WriteAllText(Path.Combine(incoming, "bad.json"), "{");
    File.WriteAllText(Path.Combine(incoming, "ignored.tmp"), "{");
    File.WriteAllBytes(Path.Combine(incoming, "z.json"), Report("next", "2026-10-15T00:00:00Z"));
    var before = Directory.GetFiles(incoming).ToDictionary(p => p, File.ReadAllBytes);
    var summary = FolderImporter.Import(store, incoming, "one", "site");
    Check(summary is { Found: 3, Imported: 1, Duplicates: 1, Rejected: 1 }, "continue_after_malformed");
    Check(before.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "source_unchanged");
    Check(store.Attempts("one").Count == 3, "attempt_history");
    var partial = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Report("partial", "2026-10-16T00:00:00Z"))!;
    partial["software"] = JsonSerializer.SerializeToElement(new { status = "error", data = (object?)null });
    store.Import(JsonSerializer.SerializeToUtf8Bytes(partial), "one", "site", "partial.json");
    Check(store.Software("one").Count == 0, "failed_section_not_stale_inventory");
    Check((string)store.Systems("one").Single()["software_status"]! == "error", "failed_section_explicit");
    partial["software"] = JsonSerializer.SerializeToElement(new { status = "ok", data = new { win32 = "invalid" } });
    Reject(() => Snapshot.Parse(JsonSerializer.SerializeToUtf8Bytes(partial)), "invalid_software_list");
    ArchivePlannerTests.Run(folder, Check);
    ArchiveMoverTests.Run(folder, Check);
    ArchiveCompressorTests.Run(folder, Check);
    MaintenanceReviewTests.Run(Check);
    SoftwareExportTests.Run(Check);
    AcquisitionWorkflowTests.Run(folder, Check);
    ConflictCleanupTests.Run(folder, Check);
    Check(store.Maintenance("one").Count == 1 && store.Maintenance("not-this-tenant").Count == 0, "maintenance_current_tenant_scope");
    Check(store.Maintenance("one", "unknown-asset").Count == 0, "maintenance_unknown_asset");
    Console.WriteLine("PASS assertions=" + assertions);
}
finally
{
    SqliteConnection.ClearAllPools();
    if (!Path.GetFullPath(folder).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("unsafe_cleanup");
    Directory.Delete(folder, true);
    Console.WriteLine("temporary_directory_removed=" + folder);
}
