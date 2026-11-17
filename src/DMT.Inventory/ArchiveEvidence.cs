using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace DMT.Inventory;

internal sealed class ArchiveEvidence : IDisposable
{
    internal sealed record Entry(string SnapshotId, string FileName, string CollectedAt, string Hash);
    private readonly SqliteConnection connection;
    private readonly string tenant;
    public ArchiveEvidence(string database, string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant)) throw new ArgumentException("tenant_required");
        this.tenant = tenant;
        connection = new(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(database), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA query_only=ON; PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) != 1) { connection.Dispose(); throw new InvalidDataException("unsupported_database_version"); }
    }
    public string Verify(string id, string expectedHash)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sha256,raw_json,collected_at FROM snapshots WHERE tenant=$tenant AND id=$id";
        command.Parameters.AddWithValue("$tenant", tenant); command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidDataException("not_imported");
        if (reader.GetString(0) != expectedHash) throw new InvalidDataException("snapshot_content_conflict");
        if (Convert.ToHexString(SHA256.HashData((byte[])reader.GetValue(1))) != expectedHash)
            throw new InvalidDataException("database_evidence_corrupt");
        return reader.GetString(2);
    }
    public IReadOnlyList<Entry> Catalog()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,source_file,collected_at,sha256 FROM snapshots WHERE tenant=$tenant";
        command.Parameters.AddWithValue("$tenant", tenant);
        using var reader = command.ExecuteReader(); var entries = new List<Entry>();
        while (reader.Read()) entries.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return entries;
    }
    public void Dispose() => connection.Dispose();
}
