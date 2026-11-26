using System.IO;
using System.Text.Json;
using DMT.Setup.Models;
using DMT.Setup.Services;

static class InstallationProgressTests
{
    public static int Run()
    {
        using var finished = new ManualResetEventSlim();
        Exception? failure = null;
        var assertions = 0;
        var worker = new Thread(() =>
        {
            try
            {
                // A blocked STA dispatcher cannot execute posted continuations, as in
                // the old synchronous progress callback. Transport must not require it.
                var context = new NonPumpingContext();
                SynchronizationContext.SetSynchronizationContext(context);
                var sink = new DelayedWriter();
                var sender = new InstallationProgressWriter(sink);
                var send = sender.SendAsync(new InstallationProgress { StatusKey = "directories", Percent = 35 });
                sink.Release.TrySetResult();
                send.GetAwaiter().GetResult();
                if (context.Posts != 0) throw new Exception("progress_depends_on_sta_dispatcher");
                assertions++;
                var progress = JsonSerializer.Deserialize<InstallationProgress>(sink.Line!);
                if (progress?.StatusKey != "directories" || progress.Percent != 35) throw new Exception("progress_protocol_mismatch");
                assertions++;
                using var cancelled = new CancellationTokenSource();
                var blocked = new InstallationProgressWriter(new DelayedWriter()).SendAsync(new InstallationProgress(), cancelled.Token);
                cancelled.Cancel();
                try { blocked.GetAwaiter().GetResult(); throw new Exception("progress_cancellation_ignored"); }
                catch (OperationCanceledException) { assertions++; }
            }
            catch (Exception error) { failure = error; }
            finally { SynchronizationContext.SetSynchronizationContext(null); finished.Set(); }
        }) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!finished.Wait(TimeSpan.FromSeconds(5))) throw new Exception("progress_sta_deadlock");
        if (failure != null) throw new Exception("progress_transport_regression", failure);
        return assertions;
    }

    private sealed class NonPumpingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state) => Interlocked.Increment(ref Posts);
    }

    private sealed class DelayedWriter : StringWriter
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Line;
        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Line = buffer.ToString();
        }
    }
}
