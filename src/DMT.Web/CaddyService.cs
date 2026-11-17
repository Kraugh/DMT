using System.Diagnostics;

namespace DMT.Web;

public sealed class CaddyService(IConfiguration config, ILogger<CaddyService> logger) : BackgroundService
{
    private Process? child;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var executable = config["Dmt:CaddyExecutable"] ?? throw new InvalidOperationException("caddy_executable_required");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("run"); start.ArgumentList.Add("--config"); start.ArgumentList.Add(config["Dmt:CaddyConfig"]!);
        start.ArgumentList.Add("--adapter"); start.ArgumentList.Add("caddyfile");
        start.Environment["XDG_DATA_HOME"] = config["Dmt:CaddyStorage"]!;
        start.Environment["XDG_CONFIG_HOME"] = config["Dmt:CaddyStorage"]!;
        using var job = new OwnedProcessJob();
        using var process = new Process { StartInfo = start }; child = process;
        if (!process.Start()) throw new InvalidOperationException("caddy_start_failed");
        job.Assign(process);
        process.OutputDataReceived += (_, e) => { if (e.Data != null) logger.LogInformation("Caddy: {Line}", e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.LogInformation("Caddy: {Line}", e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var registration = stoppingToken.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(CancellationToken.None);
        if (!stoppingToken.IsCancellationRequested)
        {
            logger.LogCritical("Caddy exited unexpectedly: {Code}", process.ExitCode);
            if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()) Environment.Exit(1);
            throw new InvalidOperationException("caddy_exited_" + process.ExitCode);
        }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try { if (child is { HasExited: false }) child.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        await base.StopAsync(cancellationToken);
    }
}
