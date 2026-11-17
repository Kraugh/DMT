using System.Text.Json;
using DMT.Inventory;

namespace DMT.Web;

public sealed class ArchiveCatchUpService(string database, string source, string tenant, string journalDirectory,
    ILogger<ArchiveCatchUpService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stamp = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Rome").ToString("yyyyMMdd-HHmmss");
                var journal = Path.Combine(journalDirectory, "DMT-monthly-archive-" + stamp + "-" + Guid.NewGuid().ToString("N")[..8] + ".jsonl");
                var summary = await Task.Run(() => ArchiveCompressor.CatchUp(database, source, tenant, journal, DateTimeOffset.UtcNow,
                    (result, elapsed) => logger.LogInformation("archive_month_result {Result} elapsedMs={Elapsed}", JsonSerializer.Serialize(result), elapsed), stoppingToken), stoppingToken);
                if (summary.UnrecognizedFiles.Count > 0) logger.LogWarning("archive_unrecognized_files count={Count}", summary.UnrecognizedFiles.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "archive_catchup_failed; retry after 15 minutes"); }
            try { await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
