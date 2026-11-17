using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

public static class ElevatedInstallationWorker
{
    public static async Task<int> RunAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(30000, cancellationToken);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        async Task Send(string key, int percent, bool completed = false, bool error = false, string? detail = null) =>
            await writer.WriteLineAsync(JsonSerializer.Serialize(new InstallationProgress { StatusKey = key, Percent = percent, IsCompleted = completed, IsError = error, Detail = detail }));
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("administrator_required");
            var line = await reader.ReadLineAsync(cancellationToken);
            var plan = line == null ? null : JsonSerializer.Deserialize<InstallationPlan>(line);
            if (plan == null) throw new InvalidDataException("installation_plan_missing");
            await Send("setup.install.progress.validating", 20);
            await LocalServiceDeployment.InstallAsync(plan, (key, percent) => Send(key, percent).GetAwaiter().GetResult(), cancellationToken);
            await Send("setup.install.progress.coreReady", 100, completed: true);
            return 0;
        }
        catch (Exception e)
        {
            var log = Path.Combine(Path.GetTempPath(), "DMT-Setup-" + Guid.NewGuid().ToString("N") + ".log");
            File.WriteAllText(log, e.ToString());
            await Send("setup.install.error.operation", 0, completed: true, error: true, detail: e.Message + Environment.NewLine + log);
            return 1;
        }
    }
}
