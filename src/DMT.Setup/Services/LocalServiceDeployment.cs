using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.Json.Serialization;
using DMT.Inventory;
using DMT.Setup.Models;
using Microsoft.Data.Sqlite;

namespace DMT.Setup.Services;

public static class LocalServiceDeployment
{
    public static readonly string ProgramDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DMT");
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DMT");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private sealed record PayloadEntry(string Source, string Destination, string Sha256);
    public static int VerifyPayload(string root)
    {
        var entries = JsonSerializer.Deserialize<PayloadEntry[]>(File.ReadAllText(Path.Combine(root, "payload-manifest.json"))) ?? throw new InvalidDataException("payload_manifest_missing");
        if (entries.Length == 0) throw new InvalidDataException("empty_payload");
        foreach (var entry in entries)
        {
            var source = Contained(root, entry.Source); Contained(ProgramDirectory, entry.Destination);
            using var stream = File.OpenRead(source);
            if (Convert.ToHexString(SHA256.HashData(stream)) != entry.Sha256) throw new InvalidDataException("payload_hash_mismatch: " + entry.Source);
        }
        foreach (var required in new[] { @"Service\DMT.Web.exe", @"Service\caddy.exe", @"Manager\DMT-Setup.exe" })
            if (!entries.Any(e => e.Destination.Equals(required, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("required_payload_missing");
        return entries.Length;
    }

    public static async Task InstallAsync(InstallationPlan plan, Func<string, int, Task> progress, CancellationToken cancellationToken,
        Action<string>? diagnostic = null)
    {
        if (plan.AccessMode != "local" || plan.HttpsMode != "internal") throw new InvalidDataException("local_internal_https_only");
        if (plan.HttpsPort is < 1 or > 65535 || string.IsNullOrWhiteSpace(plan.SourceDirectory) || !Path.IsPathFullyQualified(plan.SourceDirectory)) throw new InvalidDataException("invalid_installation_plan");
        if (!Free(plan.HttpsPort)) throw new IOException("https_port_in_use");
        if (File.Exists(Path.Combine(DataDirectory, "installation-manifest.json")) || File.Exists(Path.Combine(DataDirectory, "config", "installation.json"))) throw new IOException("existing_installation_requires_upgrade");
        if (ServiceController.GetServices().Any(s => s.ServiceName.Equals("DMT", StringComparison.OrdinalIgnoreCase))) throw new IOException("service_already_exists");
        VerifyPayload(AppContext.BaseDirectory);
        var payload = JsonSerializer.Deserialize<PayloadEntry[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "payload-manifest.json"))) ?? throw new InvalidDataException("payload_manifest_missing");
        if (payload.Length == 0) throw new InvalidDataException("empty_payload");
        var createdFiles = new List<string>(); var createdDirectories = new List<string>();
        var serviceCreated = false; string? trustedThumbprint = null;
        void DirectoryNew(string path)
        {
            diagnostic?.Invoke("directory_check " + path);
            if (Directory.Exists(path)) { if (File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("reparse_directory"); return; }
            Directory.CreateDirectory(path); createdDirectories.Add(path);
            diagnostic?.Invoke("directory_created " + path);
        }
        void WriteNew<T>(string path, T value)
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            createdFiles.Add(path); JsonSerializer.Serialize(stream, value, Json); stream.Flush(true);
        }
        try
        {
            await progress("setup.install.progress.directories", 35);
            DirectoryNew(ProgramDirectory); DirectoryNew(DataDirectory);
            diagnostic?.Invoke("program_acl_begin"); Protect(ProgramDirectory, usersRead: true);
            diagnostic?.Invoke("data_acl_begin"); Protect(DataDirectory);
            diagnostic?.Invoke("root_acls_complete");
            foreach (var name in new[] { "config", "data", "logs", "caddy" }) DirectoryNew(Path.Combine(DataDirectory, name));
            foreach (var name in new[] { "data", "logs", "caddy" })
            { diagnostic?.Invoke("service_acl " + name); Protect(Path.Combine(DataDirectory, name), serviceWrite: true); }
            await progress("setup.install.progress.deploying", 45);
            var copiedCount = 0;
            foreach (var item in payload)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Contained(ProgramDirectory, item.Destination); DirectoryNew(Path.GetDirectoryName(destination)!);
                File.Copy(Contained(AppContext.BaseDirectory, item.Source), destination, false); createdFiles.Add(destination);
                using var copied = File.OpenRead(destination);
                if (Convert.ToHexString(SHA256.HashData(copied)) != item.Sha256) throw new InvalidDataException("copied_payload_hash_mismatch");
                if (++copiedCount % 25 == 0) await progress("setup.install.progress.deploying", 45);
            }
            await progress("setup.install.progress.database", 60);
            var database = Path.Combine(DataDirectory, "data", "dmt.sqlite");
            using (new FileStream(database, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { createdFiles.Add(database); }
            if (!string.IsNullOrWhiteSpace(plan.MigrationDatabase))
            {
                using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(plan.MigrationDatabase), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
                using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
                diagnostic?.Invoke("migration_open"); input.Open(); output.Open();
                using var check = input.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
                if ((string?)check.ExecuteScalar() != "ok") throw new InvalidDataException("migration_database_corrupt");
                check.CommandText = "PRAGMA user_version";
                if (Convert.ToInt32(check.ExecuteScalar()) != 1) throw new InvalidDataException("migration_schema_not_supported");
                check.CommandText = "PRAGMA foreign_key_check";
                using (var rows = check.ExecuteReader()) if (rows.Read()) throw new InvalidDataException("migration_foreign_key_error");
                diagnostic?.Invoke("migration_backup_begin"); input.BackupDatabase(output);
                diagnostic?.Invoke("migration_backup_complete");
            }
            var verifiedStore = new InventoryStore(database); _ = verifiedStore.Systems("local"); _ = verifiedStore.Products("local"); SqliteConnection.ClearAllPools();
            var backendPort = Enumerable.Range(50080, 200).First(port => port != plan.HttpsPort && Free(port));
            var adminPath = Path.Combine(DataDirectory, "config", "admin.json");
            WriteNew(adminPath, LocalAdminCredential.Create(plan.AdminUsername, plan.AdminPassword));
            WriteNew(Path.Combine(DataDirectory, "config", "installation.json"), new
            { plan.HttpsPort, BackendPort = backendPort, plan.AccessMode, plan.HttpsMode, plan.AdminUsername, plan.AdminDisplayName, plan.SourceDirectory });
            var caddyPath = Path.Combine(DataDirectory, "config", "Caddyfile");
            using (var file = new FileStream(caddyPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file))
            {
                createdFiles.Add(caddyPath);
                writer.Write($"{{\n admin off\n auto_https disable_redirects\n skip_install_trust\n}}\nhttps://localhost:{plan.HttpsPort} {{\n bind 127.0.0.1\n tls internal\n reverse_proxy 127.0.0.1:{backendPort}\n}}\n");
            }
            var instance = Guid.NewGuid().ToString("N");
            var runtimePath = Path.Combine(DataDirectory, "config", "runtime.json");
            WriteNew(runtimePath, new Dictionary<string, string>
            {
                ["Dmt:Installed"] = "true", ["Dmt:Database"] = database, ["Dmt:Tenant"] = "local",
                ["Dmt:AdminFile"] = adminPath, ["Dmt:Instance"] = instance,
                ["Dmt:CaddyExecutable"] = Path.Combine(ProgramDirectory, "Service", "caddy.exe"),
                ["Dmt:CaddyConfig"] = caddyPath, ["Dmt:CaddyStorage"] = Path.Combine(DataDirectory, "caddy"),
                ["Dmt:ArchiveCatchUp"] = "false", ["urls"] = $"http://127.0.0.1:{backendPort}"
            });
            await progress("setup.install.progress.service", 70);
            var serviceExecutable = Path.Combine(ProgramDirectory, "Service", "DMT.Web.exe");
            await ScAsync(["create", "DMT", "binPath=", $"\"{serviceExecutable}\" --Dmt:RuntimeConfig=\"{runtimePath}\"", "start=", "auto", "obj=", @"NT AUTHORITY\LocalService", "DisplayName=", "Dashboard Maintenance Toolkit"], cancellationToken);
            serviceCreated = true;
            await ScAsync(["failure", "DMT", "reset=", "86400", "actions=", "restart/10000/restart/30000/restart/60000"], cancellationToken);
            using (var service = new ServiceController("DMT"))
            { service.Start(); await Task.Run(() => service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60)), cancellationToken); }
            await progress("setup.install.progress.certificate", 80);
            var rootPath = Path.Combine(DataDirectory, "caddy", "caddy", "pki", "authorities", "local", "root.crt");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (!File.Exists(rootPath) && DateTime.UtcNow < deadline) await Task.Delay(500, cancellationToken);
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(rootPath);
            using (var roots = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
            {
                roots.Open(OpenFlags.ReadWrite);
                if (roots.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0)
                { roots.Add(certificate); trustedThumbprint = certificate.Thumbprint; }
            }
            await progress("setup.install.progress.health", 90);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await client.GetAsync($"https://localhost:{plan.HttpsPort}/login", cancellationToken);
            if (!response.IsSuccessStatusCode || !response.Headers.TryGetValues("X-DMT-Instance", out var values) || !values.Contains(instance)) throw new IOException("installed_https_health_check_failed");
            var manager = Path.Combine(ProgramDirectory, "Manager", "DMT-Setup.exe");
            var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DMT"); DirectoryNew(menu);
            var shortcutPath = Path.Combine(menu, "DMT.lnk");
            if (File.Exists(shortcutPath)) throw new IOException("shortcut_already_exists");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath); shortcut.TargetPath = manager; shortcut.Arguments = "--manage"; shortcut.WorkingDirectory = Path.GetDirectoryName(manager); shortcut.Save(); createdFiles.Add(shortcutPath);
            var manifest = new InstallationManifest();
            foreach (var file in createdFiles) manifest.Resources.Add(new() { Type = "file", Identifier = file, CreatedByDmt = true, ManagedByDmt = true,
                RemovalPolicy = file == database ? ManagedResourceRemovalPolicy.Preserve : ManagedResourceRemovalPolicy.Remove });
            manifest.Resources.Add(new() { Type = "service", Identifier = "DMT", CreatedByDmt = true, ManagedByDmt = true });
            manifest.Resources.Add(new() { Type = "root-certificate", Identifier = certificate.Thumbprint, CreatedByDmt = trustedThumbprint != null, ManagedByDmt = trustedThumbprint != null });
            WriteNew(Path.Combine(DataDirectory, "installation-manifest.json"), manifest);
        }
        catch
        {
            diagnostic?.Invoke("rollback_begin");
            if (serviceCreated)
            {
                try { using var service = new ServiceController("DMT"); if (service.Status != ServiceControllerStatus.Stopped) { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); } } catch { }
                try { await ScAsync(["delete", "DMT"], CancellationToken.None); } catch { }
            }
            if (trustedThumbprint != null)
            {
                using var roots = new X509Store(StoreName.Root, StoreLocation.LocalMachine); roots.Open(OpenFlags.ReadWrite);
                foreach (var certificate in roots.Certificates.Find(X509FindType.FindByThumbprint, trustedThumbprint, false)) roots.Remove(certificate);
            }
            SqliteConnection.ClearAllPools();
            foreach (var file in createdFiles.AsEnumerable().Reverse()) { try { File.Delete(file); } catch { } }
            foreach (var directory in createdDirectories.AsEnumerable().Reverse()) { try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch { } }
            throw;
        }
    }
    public static string Contained(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("rooted_payload_path");
        var parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(parent, relative));
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("payload_path_escape");
        var current = Path.GetDirectoryName(full);
        while (current != null && current.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
        { if (Directory.Exists(current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("reparse_payload_path"); current = Path.GetDirectoryName(current); }
        if (File.Exists(full) && File.GetAttributes(full).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("reparse_payload_file");
        return full;
    }
    private static bool Free(int port)
    { try { var listener = new TcpListener(IPAddress.Loopback, port); listener.Start(); listener.Stop(); return true; } catch (SocketException) { return false; } }
    private static void Protect(string path, bool serviceWrite = false, bool usersRead = false)
    {
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        void Add(string sid, FileSystemRights rights) => acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), rights, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Add("S-1-5-18", FileSystemRights.FullControl); Add("S-1-5-32-544", FileSystemRights.FullControl);
        Add("S-1-5-19", serviceWrite ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute);
        if (usersRead) Add("S-1-5-32-545", FileSystemRights.ReadAndExecute);
        new DirectoryInfo(path).SetAccessControl(acl);
    }
    private static async Task ScAsync(string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        token = deadline.Token;
        using var process = Process.Start(start) ?? throw new IOException("service_command_launch_failed");
        var output = process.StandardOutput.ReadToEndAsync(token); var errors = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { try { if (!process.HasExited) process.Kill(); } catch { } throw; }
        if (process.ExitCode != 0) throw new IOException("service_command_failed: " + await output + await errors);
    }
}
