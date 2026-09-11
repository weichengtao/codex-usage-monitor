using System.Text.Json;
using CodexUsageMonitor.Core;
using CodexUsageMonitor;

if (args.FirstOrDefault() == "app-server") { await FakeServer.RunAsync(); return; }

int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}"); passed++;
}
UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, DateTimeOffset.UtcNow); }

var snapshot = Parse("""
{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},
 "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":35,"windowDurationMins":10080,"resetsAt":1800000000},"secondary":{"usedPercent":17.5,"windowDurationMins":300}}},
 "rateLimitResetCredits":{"availableCount":3,"credits":[]}}
""");
Check(snapshot.FiveHour?.Remaining == 82.5 && snapshot.Weekly?.Remaining == 65, "Prefer Codex bucket and identify reversed windows by duration");
Check(snapshot.Resets == 3, "Authoritative count wins over empty credit detail rows");
Check(snapshot.Weekly?.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1800000000), "Unix reset time conversion");
snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":-5,"windowDurationMins":300},"secondary":{"usedPercent":105,"windowDurationMins":10080}},"rateLimitResetCredits":null}""");
Check(snapshot.FiveHour?.Remaining == 100 && snapshot.Weekly?.Remaining == 0, "Clamp out-of-range usage");
Check(snapshot.Resets == null, "Null reset count remains unknown");
snapshot = Parse("""{"rateLimits":{"primary":null,"secondary":{"usedPercent":50,"windowDurationMins":60}},"rateLimitResetCredits":{"availableCount":0}}""");
Check(snapshot.FiveHour == null && snapshot.Weekly == null && snapshot.Resets == 0, "Unknown duration is not guessed; zero resets is known");
bool rejected = false;
try { Parse("""{"rateLimits":{"limitId":"other"},"rateLimitsByLimitId":{"other":{}}}"""); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "Do not show another bucket as Codex usage");
snapshot = Parse("""{"rateLimits":{"primary":{"usedPercent":null,"windowDurationMins":null}}}""");
Check(snapshot.FiveHour == null, "Null window fields are unavailable");

for (int count = 0; count <= 5; count++)
{
    var full = IconLayout.Create(100, 100, count);
    Check(count == 0 ? full.FilledDegrees == 360 : full.FilledDegrees < 360, $"Full weekly ring respects gap for {count} resets");
    Check(full.DotDegrees.Count == count && (count == 0 || Math.Abs(full.DotDegrees.Average() - 90) < .001), $"{count} dots centered at bottom");
    var half = IconLayout.Create(50, 8, count);
    Check(Math.Abs(half.FilledDegrees * 2 - full.FilledDegrees) < .001, $"Weekly progress is relative to available arc for {count} resets");
}
Check(IconLayout.Create(0, 0, 1).FilledDegrees == 0, "Zero weekly quota produces no progress arc");
Check(IconLayout.Create(100, 82.5, 2).CenterText == "83", "Center rounds remaining quota without percent sign");
Check(IconLayout.Create(null, null, null).CenterText == "—", "Unknown center does not claim zero");
Check(IconLayout.Create(100, 100, 8).DotDegrees.Count == 8, "Higher reset counts retain one dot per reset");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_MODE", "transport");
await using (var client = new AppServerClient())
{
    var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.UsageChanged += () => notification.TrySetResult();
    await client.StartAsync(Environment.ProcessPath, timeout.Token);
    var first = client.ReadUsageAsync(timeout.Token);
    var second = client.ReadUsageAsync(timeout.Token);
    var responses = await Task.WhenAll(first, second);
    Check(responses[0].GetProperty("sequence").GetInt32() == 1 && responses[1].GetProperty("sequence").GetInt32() == 2,
        "Transport correlates out-of-order responses by request ID");
    await notification.Task.WaitAsync(timeout.Token);
    Check(true, "Transport receives account update notification");
    bool disconnected = false;
    try { await client.ReadUsageAsync(timeout.Token); } catch (IOException) { disconnected = true; }
    Check(disconnected, "Server exit fails pending requests promptly");
}

Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_MODE", "monitor");
await using (var monitor = new UsageMonitor(() => Environment.ProcessPath))
{
    var initial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    int successfulReads = 0;
    monitor.Changed += state =>
    {
        if (state.Error != null && state.Snapshot != null) failed.TrySetResult();
        if (!state.Connecting && state.Error == null && state.Snapshot != null)
        {
            if (Interlocked.Increment(ref successfulReads) == 1) initial.TrySetResult();
            else recovered.TrySetResult();
        }
    };
    monitor.Start();
    await initial.Task.WaitAsync(timeout.Token);
    Check(monitor.State.Snapshot?.Resets == 2, "Monitor reads quota from persistent server");
    monitor.Refresh();
    await failed.Task.WaitAsync(timeout.Token);
    Check(monitor.State.IsStale && monitor.State.Snapshot?.FiveHour?.Remaining == 80, "Disconnection retains quota and marks it stale");
    await recovered.Task.WaitAsync(timeout.Token);
    Check(!monitor.State.IsStale, "Monitor automatically restarts server and clears stale state");
}
Environment.SetEnvironmentVariable("CODEX_MONITOR_TEST_MODE", null);
Console.WriteLine($"{passed} checks passed.");
