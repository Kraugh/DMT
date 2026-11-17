using System.Text;
using System.Text.Json;

namespace DMT.Inventory;

internal sealed class ArchiveJournal : IDisposable
{
    private readonly FileStream stream;
    public ArchiveJournal(string path)
    {
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new InvalidDataException("archive_journal_must_be_local");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
    public void Write(object entry)
    {
        lock (stream)
        {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow.ToString("O"), entry }) + "\n");
        stream.Write(bytes); stream.Flush(true);
        }
    }
    public void Dispose() => stream.Dispose();
}
