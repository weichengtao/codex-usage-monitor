using System.Threading.Channels;
using CodexUsageMonitor.Core;

namespace CodexUsageMonitor;

internal sealed record MonitorState(UsageSnapshot? Snapshot, string? Error, bool Connecting)
{
    public bool IsStale => Error != null || Snapshot == null || DateTimeOffset.UtcNow - Snapshot.FetchedAt > TimeSpan.FromMinutes(3);
}

internal sealed class UsageMonitor : IAsyncDisposable
{
    private readonly Channel<bool> refresh = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<string?> executable;
    private Task? loop;
    public MonitorState State { get; private set; } = new(null, null, true);
    public event Action<MonitorState>? Changed;
    public UsageMonitor(Func<string?> executable) => this.executable = executable;
    public void Start() => loop = Task.Run(RunAsync);
    public void Refresh() => refresh.Writer.TryWrite(true);

    private async Task RunAsync()
    {
        AppServerClient? client = null;
        string? activeExecutable = null;
        int failures = 0;
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                while (refresh.Reader.TryRead(out _)) { }
                try
                {
                    var requested = executable();
                    if (client != null && requested != activeExecutable)
                    {
                        await client.DisposeAsync();
                        client = null;
                    }
                    if (client == null)
                    {
                        Publish(State with { Connecting = true });
                        client = new AppServerClient();
                        client.UsageChanged += Refresh;
                        activeExecutable = requested;
                        await client.StartAsync(requested, lifetime.Token);
                    }
                    var response = await client.ReadUsageAsync(lifetime.Token);
                    Publish(new(UsageSnapshot.Parse(response, DateTimeOffset.UtcNow), null, false));
                    failures = 0;
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    failures++;
                    Publish(State with { Error = ex is TimeoutException ? "Codex did not respond. Retrying…" : ex.Message, Connecting = false });
                    if (client != null) { await client.DisposeAsync(); client = null; }
                }
                // Coalesce requests, but preserve a refresh that arrived during the read.
                var delay = failures == 0 ? 60 : Math.Min(60, 5 * Math.Pow(2, Math.Min(failures - 1, 4)));
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                wait.CancelAfter(TimeSpan.FromSeconds(delay));
                try { await refresh.Reader.ReadAsync(wait.Token); }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { if (client != null) await client.DisposeAsync(); }
    }

    private void Publish(MonitorState state) { State = state; Changed?.Invoke(state); }
    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (loop != null) await loop;
        lifetime.Dispose();
    }
}
