using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using DMT.Setup.Models;
using System.IO;

namespace DMT.Setup.Services;

public static class ElevatedInstallationWorker
{
    public static async Task<int> RunAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);

            await pipe.ConnectAsync(30000, cancellationToken);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            var line = await reader.ReadLineAsync(cancellationToken);
            var plan = line is null ? null : JsonSerializer.Deserialize<InstallationPlan>(line);
            if (plan is null)
            {
                await SendAsync(writer, "setup.install.error.plan", 0, completed: true, error: true);
                return 2;
            }

            await SendAsync(writer, "setup.install.progress.elevated", 20);

            if (!IsAdministrator())
            {
                await SendAsync(writer, "setup.install.error.notElevated", 20, completed: true, error: true);
                return 3;
            }

            await SendAsync(writer, "setup.install.progress.validating", 45);
            if (!ValidatePlan(plan))
            {
                await SendAsync(writer, "setup.install.error.plan", 45, completed: true, error: true);
                return 4;
            }

            await SendAsync(writer, "setup.install.progress.manifest", 70);

            // Foundation only: prepare the ownership model in memory. No system resource is changed yet.
            _ = BuildPlannedManifest(plan);

            await Task.Delay(250, cancellationToken);
            await SendAsync(writer, "setup.install.progress.foundationReady", 100, completed: true);
            return 0;
        }
        catch
        {
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

    private static InstallationManifest BuildPlannedManifest(InstallationPlan plan)
    {
        var manifest = new InstallationManifest();
        manifest.Resources.Add(new ManagedResourceRecord { Type = "directory", Identifier = @"C:\Program Files\DMT", CreatedByDmt = true, ManagedByDmt = true });
        manifest.Resources.Add(new ManagedResourceRecord { Type = "data-directory", Identifier = @"C:\ProgramData\DMT", CreatedByDmt = true, ManagedByDmt = true });
        manifest.Resources.Add(new ManagedResourceRecord { Type = "service", Identifier = "DMT Server", CreatedByDmt = true, ManagedByDmt = true });
        manifest.Resources.Add(new ManagedResourceRecord { Type = "caddy", Identifier = "DMT-managed Caddy", CreatedByDmt = true, ManagedByDmt = true, RemovalPolicy = ManagedResourceRemovalPolicy.AskDefaultRemove });
        if (plan.AccessMode != "local")
            manifest.Resources.Add(new ManagedResourceRecord { Type = "firewall-rule", Identifier = $"DMT HTTPS {plan.HttpsPort}", CreatedByDmt = true, ManagedByDmt = true });
        return manifest;
    }

    private static Task SendAsync(StreamWriter writer, string statusKey, int percent, bool completed = false, bool error = false) =>
        writer.WriteLineAsync(JsonSerializer.Serialize(new InstallationProgress
        {
            StatusKey = statusKey,
            Percent = percent,
            IsCompleted = completed,
            IsError = error
        }));
}
