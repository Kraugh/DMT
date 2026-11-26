using System.IO;
using System.Text.Json;
using DMT.Setup.Models;

namespace DMT.Setup.Services;

/// <summary>Awaitable progress transport; never synchronously waits on a WPF dispatcher.</summary>
public sealed class InstallationProgressWriter(TextWriter writer)
{
    public async Task SendAsync(InstallationProgress progress, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        await writer.WriteLineAsync(JsonSerializer.Serialize(progress).AsMemory(), deadline.Token)
            .WaitAsync(deadline.Token).ConfigureAwait(false);
    }
}
