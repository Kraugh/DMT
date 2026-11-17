using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DMT.Inventory;

public sealed class InventoryStore
{
    private readonly string connectionString;
    public InventoryStore(string path)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = full, ForeignKeys = true, DefaultTimeout = 30 }.ToString();
        using var connection = Open();
        Execute(connection, null, "PRAGMA journal_mode=WAL;");
        using var transaction = connection.BeginTransaction();
        var version = Convert.ToInt32(Scalar(connection, transaction, "PRAGMA user_version;"));
        if (version > 1) throw new InvalidDataException("unsupported_database_version");
        if (version == 0) Execute(connection, transaction, """
            CREATE TABLE assets(tenant TEXT NOT NULL,id TEXT NOT NULL,PRIMARY KEY(tenant,id));
            CREATE TABLE identifiers(tenant TEXT NOT NULL,kind TEXT NOT NULL,value TEXT NOT NULL,asset_id TEXT NOT NULL,
              PRIMARY KEY(tenant,kind,value),FOREIGN KEY(tenant,asset_id) REFERENCES assets(tenant,id));
            CREATE TABLE snapshots(tenant TEXT NOT NULL,id TEXT NOT NULL,asset_id TEXT NOT NULL,site TEXT NOT NULL,
              collected_at TEXT NOT NULL,hostname TEXT NOT NULL,manufacturer TEXT NOT NULL,uuid TEXT,serial TEXT,
              os_name TEXT,os_version TEXT,os_build TEXT,os_status TEXT,software_status TEXT,maintenance_status TEXT,
              collection_status TEXT,sections_json TEXT NOT NULL,source_file TEXT NOT NULL,sha256 TEXT NOT NULL,
              raw_json BLOB NOT NULL,imported_at TEXT NOT NULL,PRIMARY KEY(tenant,id),
              FOREIGN KEY(tenant,asset_id) REFERENCES assets(tenant,id));
            CREATE INDEX ix_snapshots_current ON snapshots(tenant,asset_id,collected_at DESC,id DESC);
            CREATE TABLE software(tenant TEXT NOT NULL,snapshot_id TEXT NOT NULL,name TEXT NOT NULL,publisher TEXT,
              version TEXT,source TEXT NOT NULL,FOREIGN KEY(tenant,snapshot_id) REFERENCES snapshots(tenant,id));
            CREATE INDEX ix_software_product ON software(tenant,name,publisher,version);
            CREATE INDEX ix_software_snapshot ON software(tenant,snapshot_id);
            CREATE TABLE import_attempts(id INTEGER PRIMARY KEY,tenant TEXT NOT NULL,site TEXT NOT NULL,
              source_file TEXT NOT NULL,attempted_at TEXT NOT NULL,result TEXT NOT NULL,code TEXT);
            CREATE VIEW current_snapshots AS SELECT * FROM snapshots s WHERE NOT EXISTS
              (SELECT 1 FROM snapshots newer WHERE newer.tenant=s.tenant AND newer.asset_id=s.asset_id AND
              (newer.collected_at>s.collected_at OR (newer.collected_at=s.collected_at AND newer.id>s.id)));
            PRAGMA user_version=1;
            """);
        transaction.Commit();
    }

    public string Import(byte[] bytes, string tenant, string site, string source)
    {
        RequireContext(tenant, site);
        var snapshot = Snapshot.Parse(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = Scalar(connection, transaction, "SELECT sha256 FROM snapshots WHERE tenant=$tenant AND id=$id", ("tenant", tenant), ("id", snapshot.Id));
        if (previous is string oldHash)
        {
            if (oldHash != hash) throw new InvalidDataException("snapshot_id_conflict");
            transaction.Commit();
            return "duplicate";
        }
        var identities = new List<(string Kind, string Value)>();
        if (snapshot.Uuid != null) identities.Add(("uuid", snapshot.Uuid));
        if (snapshot.Serial != null) identities.Add(("serial", snapshot.Manufacturer.ToUpperInvariant() + ":" + snapshot.Serial));
        var matches = identities.Select(identity => Scalar(connection, transaction,
            "SELECT asset_id FROM identifiers WHERE tenant=$tenant AND kind=$kind AND value=$value",
            ("tenant", tenant), ("kind", identity.Kind), ("value", identity.Value)) as string).OfType<string>().Distinct().ToArray();
        if (matches.Length > 1) throw new InvalidDataException("identity_conflict");
        var assetId = matches.FirstOrDefault() ?? Guid.NewGuid().ToString("D");
        if (matches.Length == 0) Execute(connection, transaction, "INSERT INTO assets VALUES($tenant,$asset)", ("tenant", tenant), ("asset", assetId));
        // Do not silently attach a new UUID to an existing serial: this can merge different machines.
        foreach (var identity in identities)
        {
            if (identity.Kind == "uuid" && Scalar(connection, transaction,
                "SELECT value FROM identifiers WHERE tenant=$tenant AND asset_id=$asset AND kind='uuid' AND value<>$value",
                ("tenant", tenant), ("asset", assetId), ("value", identity.Value)) is string)
                throw new InvalidDataException("identity_conflict");
            Execute(connection, transaction, "INSERT OR IGNORE INTO identifiers VALUES($tenant,$kind,$value,$asset)",
                ("tenant", tenant), ("kind", identity.Kind), ("value", identity.Value), ("asset", assetId));
        }
        Execute(connection, transaction, """
            INSERT INTO snapshots VALUES($tenant,$id,$asset,$site,$date,$hostname,$manufacturer,$uuid,$serial,
              $osName,$osVersion,$osBuild,$osStatus,$softwareStatus,$maintenance,$collection,$sections,$source,$hash,$raw,$imported)
            """, ("tenant", tenant), ("id", snapshot.Id), ("asset", assetId), ("site", site), ("date", snapshot.CollectedAt),
            ("hostname", snapshot.Hostname), ("manufacturer", snapshot.Manufacturer), ("uuid", snapshot.Uuid), ("serial", snapshot.Serial),
            ("osName", snapshot.OsName), ("osVersion", snapshot.OsVersion), ("osBuild", snapshot.OsBuild), ("osStatus", snapshot.OsStatus),
            ("softwareStatus", snapshot.SoftwareStatus), ("maintenance", snapshot.MaintenanceStatus), ("collection", snapshot.CollectionStatus),
            ("sections", snapshot.SectionsJson), ("source", source), ("hash", hash), ("raw", bytes), ("imported", DateTimeOffset.UtcNow.ToString("O")));
        foreach (var item in snapshot.Software.Distinct())
            Execute(connection, transaction, "INSERT INTO software VALUES($tenant,$snapshot,$name,$publisher,$version,$source)",
                ("tenant", tenant), ("snapshot", snapshot.Id), ("name", item.Name), ("publisher", item.Publisher), ("version", item.Version), ("source", item.Source));
        transaction.Commit();
        return "imported";
    }

    public void RecordAttempt(string tenant, string site, string source, string result, string? code)
    {
        RequireContext(tenant, site);
        using var connection = Open();
        Execute(connection, null, "INSERT INTO import_attempts(tenant,site,source_file,attempted_at,result,code) VALUES($tenant,$site,$source,$date,$result,$code)",
            ("tenant", tenant), ("site", site), ("source", source), ("date", DateTimeOffset.UtcNow.ToString("O")), ("result", result), ("code", code));
    }

    public IReadOnlyList<Dictionary<string, object?>> Systems(string tenant, string? site = null, string? name = null, string? asset = null) => Query("""
        SELECT s.asset_id,s.id AS snapshot_id,s.site,s.hostname,s.uuid,s.serial,s.collected_at,s.os_name,s.os_version,s.os_build,
          s.os_status,s.software_status,s.maintenance_status,s.collection_status
        FROM current_snapshots s WHERE s.tenant=$tenant AND ($site IS NULL OR s.site=$site)
          AND ($name IS NULL OR instr(lower(coalesce(s.os_name,'')),lower($name))>0)
          AND ($asset IS NULL OR s.asset_id=$asset) ORDER BY s.hostname,s.asset_id
        """, ("tenant", tenant), ("site", site), ("name", name), ("asset", asset));

    public IReadOnlyList<Dictionary<string, object?>> Software(string tenant, string? site = null, string? name = null,
        string? publisher = null, string? version = null, string? asset = null) => Query("""
        SELECT DISTINCT s.asset_id,s.hostname,s.id AS snapshot_id,w.name,w.publisher,w.version,w.source,s.software_status
        FROM current_snapshots s JOIN software w ON w.tenant=s.tenant AND w.snapshot_id=s.id
        WHERE s.tenant=$tenant AND ($site IS NULL OR s.site=$site) AND ($asset IS NULL OR s.asset_id=$asset)
          AND ($name IS NULL OR instr(lower(w.name),lower($name))>0)
          AND ($publisher IS NULL OR instr(lower(coalesce(w.publisher,'')),lower($publisher))>0)
          AND ($version IS NULL OR instr(lower(coalesce(w.version,'')),lower($version))>0)
        ORDER BY w.name,w.publisher,w.version,s.hostname
        """, ("tenant", tenant), ("site", site), ("asset", asset), ("name", name), ("publisher", publisher), ("version", version));

    public IReadOnlyList<Dictionary<string, object?>> Products(string tenant, string? site = null) => Query("""
        SELECT w.name COLLATE NOCASE AS name,w.publisher COLLATE NOCASE AS publisher,COUNT(DISTINCT s.asset_id) AS computers
        FROM current_snapshots s JOIN software w ON w.tenant=s.tenant AND w.snapshot_id=s.id
        WHERE s.tenant=$tenant AND ($site IS NULL OR s.site=$site)
        GROUP BY w.name COLLATE NOCASE,w.publisher COLLATE NOCASE ORDER BY name,publisher
        """, ("tenant", tenant), ("site", site));

    public IReadOnlyList<Dictionary<string, object?>> History(string tenant, string asset) => Query("""
        SELECT id AS snapshot_id,collected_at,hostname,site,os_status,software_status,maintenance_status,collection_status,sections_json
        FROM snapshots WHERE tenant=$tenant AND asset_id=$asset ORDER BY collected_at DESC,id DESC
        """, ("tenant", tenant), ("asset", asset));

    public IReadOnlyList<Dictionary<string, object?>> Attempts(string tenant) => Query("""
        SELECT attempted_at,result,code FROM import_attempts WHERE tenant=$tenant ORDER BY id DESC LIMIT 200
        """, ("tenant", tenant));

    public IReadOnlyList<MaintenanceReview> Maintenance(string tenant, string? asset = null) => Query("""
        SELECT asset_id,id,hostname,collected_at,sections_json FROM current_snapshots
        WHERE tenant=$tenant AND ($asset IS NULL OR asset_id=$asset) ORDER BY hostname,asset_id
        """, ("tenant", tenant), ("asset", asset)).Select(row => MaintenanceReviewReader.Read(
            (string)row["asset_id"]!, (string)row["id"]!, (string)row["hostname"]!,
            (string)row["collected_at"]!, (string)row["sections_json"]!)).ToArray();

    private IReadOnlyList<Dictionary<string, object?>> Query(string sql, params (string, object?)[] parameters)
    {
        if (!parameters.Any(p => p.Item1 == "tenant" && p.Item2 is string value && !string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("tenant_required");
        using var connection = Open();
        using var command = Command(connection, null, sql, parameters);
        using var reader = command.ExecuteReader();
        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
    private SqliteConnection Open() { var connection = new SqliteConnection(connectionString); connection.Open(); return connection; }
    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (key, value) in parameters) command.Parameters.AddWithValue("$" + key, value ?? DBNull.Value);
        return command;
    }
    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    { using var command = Command(connection, transaction, sql, parameters); return command.ExecuteScalar(); }
    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string, object?)[] parameters)
    { using var command = Command(connection, transaction, sql, parameters); command.ExecuteNonQuery(); }
    private static void RequireContext(string tenant, string site)
    { if (string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(site)) throw new ArgumentException("tenant_and_site_required"); }
}
