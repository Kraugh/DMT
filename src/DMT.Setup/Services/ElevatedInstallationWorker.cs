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
        var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMT", "SetupLogs");
        Directory.CreateDirectory(logDirectory);
        var log = Path.Combine(logDirectory, "DMT-Setup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".log");
        using var logFile = new FileStream(log, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var trace = new StreamWriter(logFile) { AutoFlush = true };
        void Record(string message)
        {
            trace.WriteLine(DateTimeOffset.UtcNow.ToString("O") + " " + message);
            trace.Flush(); logFile.Flush(true);
        }
        Record("worker_started version=" + typeof(ElevatedInstallationWorker).Assembly.GetName().Version + " pid=" + Environment.ProcessId);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        InstallationProgressWriter? progressWriter = null;
        StreamWriter? writer = null;
        async Task Send(string key, int percent, bool completed = false, bool error = false, string? detail = null)
        {
            Record("progress " + key + " percent=" + percent);
            if (progressWriter != null)
                await progressWriter.SendAsync(new InstallationProgress { StatusKey = key, Percent = percent, IsCompleted = completed, IsError = error, Detail = detail ?? log }, cancellationToken);
        }
        try
        {
            await pipe.ConnectAsync(30000, cancellationToken);
            Record("pipe_connected");
            using var reader = new StreamReader(pipe, leaveOpen: true);
            writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            progressWriter = new InstallationProgressWriter(writer);
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("administrator_required");
            var line = await reader.ReadLineAsync(cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            var plan = line == null ? null : JsonSerializer.Deserialize<InstallationPlan>(line);
            if (plan == null) throw new InvalidDataException("installation_plan_missing");
            await Send("setup.install.progress.validating", 20);
            await LocalServiceDeployment.InstallAsync(plan, (key, percent) => Send(key, percent), cancellationToken, Record);
            await Send("setup.install.progress.coreReady", 100, completed: true);
            Record("worker_completed");
            return 0;
        }
        catch (Exception e)
        {
            Record("worker_failed " + e);
            try { await Send("setup.install.error.operation", 0, completed: true, error: true, detail: e.Message + Environment.NewLine + log); }
            catch (Exception notificationError) { Record("notification_failed " + notificationError.GetType().Name); }
            return 1;
        }
        finally
        {
            try { writer?.Dispose(); } catch (IOException) { }
        }
    }
}
