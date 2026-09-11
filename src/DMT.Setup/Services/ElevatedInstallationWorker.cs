using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public static class ElevatedInstallationWorker
{
    private const string ProgramDirectory = @"C:\Program Files\DMT";
    private const string DataDirectory = @"C:\ProgramData\DMT";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        var createdDirectories = new List<string>();
        var createdFiles = new List<string>();

        try
        {
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(30000, cancellationToken);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(cancellationToken);
            var plan = line is null ? null : JsonSerializer.Deserialize<InstallationPlan>(line);
            if (plan is null) { await SendAsync(writer, "setup.install.error.plan", 0, true, true); return 2; }

            await SendAsync(writer, "setup.install.progress.elevated", 15);
            if (!IsAdministrator()) { await SendAsync(writer, "setup.install.error.notElevated", 15, true, true); return 3; }

            await SendAsync(writer, "setup.install.progress.validating", 30);
            if (!ValidatePlan(plan)) { await SendAsync(writer, "setup.install.error.plan", 30, true, true); return 4; }

            await SendAsync(writer, "setup.install.progress.directories", 50);
            EnsureDirectory(ProgramDirectory, createdDirectories);
            EnsureDirectory(DataDirectory, createdDirectories);
            EnsureDirectory(Path.Combine(DataDirectory, "config"), createdDirectories);
            EnsureDirectory(Path.Combine(DataDirectory, "data"), createdDirectories);
            EnsureDirectory(Path.Combine(DataDirectory, "logs"), createdDirectories);

            await SendAsync(writer, "setup.install.progress.configuration", 70);
            var configPath = Path.Combine(DataDirectory, "config", "installation.json");
            var config = new PersistedInstallationConfiguration
            {
                HttpsPort = plan.HttpsPort,
                AccessMode = plan.AccessMode,
                AccessAddress = plan.AccessAddress,
                HttpsMode = plan.HttpsMode,
                AdminUsername = plan.AdminUsername,
                AdminDisplayName = plan.AdminDisplayName
            };
            WriteNewJsonFile(configPath, config, createdFiles);

            await SendAsync(writer, "setup.install.progress.manifest", 85);
            var manifest = BuildInstalledManifest(createdDirectories, configPath);
            var manifestPath = Path.Combine(DataDirectory, "installation-manifest.json");
            WriteNewJsonFile(manifestPath, manifest, createdFiles);

            await SendAsync(writer, "setup.install.progress.coreReady", 100, completed: true);
            return 0;
        }
        catch
        {
            RollBack(createdFiles, createdDirectories);
            return 1;
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool ValidatePlan(InstallationPlan plan) =>
        plan.HttpsPort is >= 1 and <= 65535
        && !string.IsNullOrWhiteSpace(plan.AdminUsername)
        && plan.AdminPassword.Length >= 12
        && plan.AccessMode is "local" or "lan" or "custom"
        && plan.HttpsMode is "internal" or "existing"
        && (plan.AccessMode != "custom" || !string.IsNullOrWhiteSpace(plan.AccessAddress));

    private static void EnsureDirectory(string path, ICollection<string> createdDirectories)
    {
        if (Directory.Exists(path)) return;
        Directory.CreateDirectory(path);
        createdDirectories.Add(path);
    }

    private static void WriteNewJsonFile<T>(string path, T value, ICollection<string> createdFiles)
    {
        if (File.Exists(path))
            throw new IOException($"DMT refuses to overwrite an existing installation resource: {path}");

        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
        createdFiles.Add(path);
    }

    private static InstallationManifest BuildInstalledManifest(IReadOnlyCollection<string> createdDirectories, string configPath)
    {
        var manifest = new InstallationManifest();

        AddCreatedDirectory(manifest, createdDirectories, ProgramDirectory, "directory");
        AddCreatedDirectory(manifest, createdDirectories, DataDirectory, "data-directory");
        AddCreatedDirectory(manifest, createdDirectories, Path.Combine(DataDirectory, "config"), "data-directory");
        AddCreatedDirectory(manifest, createdDirectories, Path.Combine(DataDirectory, "data"), "data-directory");
        AddCreatedDirectory(manifest, createdDirectories, Path.Combine(DataDirectory, "logs"), "log-directory");

        manifest.Resources.Add(new ManagedResourceRecord
        {
            Type = "configuration",
            Identifier = configPath,
            CreatedByDmt = true,
            ManagedByDmt = true
        });

        return manifest;
    }

    private static void AddCreatedDirectory(InstallationManifest manifest, IReadOnlyCollection<string> createdDirectories, string path, string type)
    {
        if (!createdDirectories.Contains(path)) return;

        manifest.Resources.Add(new ManagedResourceRecord
        {
            Type = type,
            Identifier = path,
            CreatedByDmt = true,
            ManagedByDmt = true
        });
    }

    private static void RollBack(IEnumerable<string> createdFiles, IEnumerable<string> createdDirectories)
    {
        foreach (var file in createdFiles.Reverse())
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
        foreach (var directory in createdDirectories.Reverse())
        {
            try { if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch { }
        }
    }

    private static Task SendAsync(StreamWriter writer, string statusKey, int percent, bool completed = false, bool error = false) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(new InstallationProgress { StatusKey = statusKey, Percent = percent, IsCompleted = completed, IsError = error }));

    private sealed class PersistedInstallationConfiguration
    {
        public int HttpsPort { get; init; }
        public string AccessMode { get; init; } = string.Empty;
        public string? AccessAddress { get; init; }
        public string HttpsMode { get; init; } = string.Empty;
        public string AdminUsername { get; init; } = string.Empty;
        public string? AdminDisplayName { get; init; }
    }
}
