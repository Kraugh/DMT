using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace DMT.Inventory;

internal static class ArchiveFiles
{
    public static FileStream OpenForVerifiedDeletion(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("verified_archive_requires_windows");
        var handle = CreateFileW(path, 0x80010000, 1, IntPtr.Zero, 3, 0x00200080, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error is 2 or 3) throw new FileNotFoundException("source_missing", path);
            throw new IOException("source_open_failed", new Win32Exception(error));
        }
        try
        {
            if (!GetFileInformationByHandleEx(handle, 9, out var tags, (uint)Marshal.SizeOf<AttributeTagInfo>()))
                throw new IOException("source_attributes_failed", new Win32Exception(Marshal.GetLastWin32Error()));
            if ((tags.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("reparse_point");
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }
    public static void DeleteLocked(FileStream stream)
    {
        var disposition = new DispositionInfo { Delete = 1 };
        if (!SetFileInformationByHandle(stream.SafeFileHandle, 4, ref disposition, 1))
            throw new IOException("verified_source_delete_failed", new Win32Exception(Marshal.GetLastWin32Error()));
    }
    public static void VerifyHash(Stream stream, string expected)
    {
        stream.Position = 0;
        if (Convert.ToHexString(SHA256.HashData(stream)) != expected) throw new InvalidDataException("file_hash_changed");
        stream.Position = 0;
    }
    public static FileStream OpenRead(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("reparse_point");
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    public static void ValidateDirectory(string path)
    {
        var attributes = File.GetAttributes(path);
        if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("unsafe_archive_directory");
    }
    public static FileStream LockDirectory(string archive)
    {
        ValidateDirectory(archive);
        var path = Path.Combine(archive, ".dmt-archive.lock");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("unsafe_archive_lock");
        // An interrupted SMB session may leave the file behind. Reopen it exclusively:
        // an actually held lock still fails, while an abandoned file no longer blocks recovery.
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
    }
    [StructLayout(LayoutKind.Sequential)] private struct DispositionInfo { public byte Delete; }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint Attributes; public uint Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref DispositionInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo info, uint size);
}
