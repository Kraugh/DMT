using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using DMT.Setup.Models;
using System.IO;

namespace DMT.Setup.Services;

public sealed class ElevatedInstallationService
{
    public async Task<ElevatedInstallationResult> RunAsync(
        InstallationPlan plan,
        Action<InstallationProgress> onProgress,
        CancellationToken cancellationToken = default)
    {
        var pipeName = $"DMT.Setup.{Guid.NewGuid():N}";

        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return ElevatedInstallationResult.Failed("setup.install.error.launch");

        Process? helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--elevated-install {pipeName}",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ElevatedInstallationResult.CancelledByUser();
        }
        catch
        {
            return ElevatedInstallationResult.Failed("setup.install.error.launch");
        }

        if (helper is null)
            return ElevatedInstallationResult.Failed("setup.install.error.launch");

        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(plan));

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null) break;

                var progress = JsonSerializer.Deserialize<InstallationProgress>(line);
                if (progress is null) continue;

                onProgress(progress);
                if (progress.IsCompleted)
                    return progress.IsError
                        ? ElevatedInstallationResult.Failed(progress.StatusKey)
                        : ElevatedInstallationResult.Succeeded();
            }
        }
        catch (OperationCanceledException)
        {
            return ElevatedInstallationResult.Failed("setup.install.error.cancelled");
        }
        catch
        {
            return ElevatedInstallationResult.Failed("setup.install.error.communication");
        }

        return ElevatedInstallationResult.Failed("setup.install.error.communication");
    }
}

public sealed record ElevatedInstallationResult(bool Success, bool UacCancelled, string? ErrorKey)
{
    public static ElevatedInstallationResult Succeeded() => new(true, false, null);
    public static ElevatedInstallationResult CancelledByUser() => new(false, true, "setup.install.uac.cancelled");
    public static ElevatedInstallationResult Failed(string errorKey) => new(false, false, errorKey);
}
