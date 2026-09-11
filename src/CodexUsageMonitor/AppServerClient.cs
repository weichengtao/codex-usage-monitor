using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexUsageMonitor;

internal sealed class AppServerClient : IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private Task? readerTask, errorTask;
    private int nextId;
    public event Action? UsageChanged;

    public static string FindCodex(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) && configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(configured) : throw new FileNotFoundException("Choose an existing codex.exe in Settings.");
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory.Trim('"'), "codex.exe");
            if (File.Exists(candidate)) return candidate;
        }
        var appBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        if (Directory.Exists(appBin))
        {
            var candidate = Directory.EnumerateFiles(appBin, "codex.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (candidate != null) return candidate;
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
        if (Directory.Exists(npm))
        {
            var candidate = Directory.EnumerateFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (candidate != null) return candidate;
        }
        throw new FileNotFoundException("Codex CLI was not found. Set its executable path in Settings.");
    }

    public async Task StartAsync(string? executable, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(FindCodex(executable))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("app-server");
        start.ArgumentList.Add("--stdio");
        // Some desktop launch environments omit HOME. Let Codex reuse the normal Windows profile.
        if (!start.Environment.TryGetValue("HOME", out var home) || string.IsNullOrWhiteSpace(home))
            start.Environment["HOME"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        process = Process.Start(start) ?? throw new IOException("Could not start Codex app-server.");
        readerTask = ReadLoopAsync();
        // Drain stderr to prevent deadlocks; never persist potentially sensitive server output.
        errorTask = DrainErrorsAsync();
        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex_usage_monitor", title = "Codex Usage Monitor", version = "1.0.0" },
            capabilities = new { experimentalApi = true }
        }, cancellationToken);
        await SendAsync(new { method = "initialized" }, cancellationToken);
    }

    public Task<JsonElement> ReadUsageAsync(CancellationToken cancellationToken) =>
        RequestAsync("account/rateLimits/read", null, cancellationToken);

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        if (process == null || process.HasExited) throw new IOException("Codex app-server is not running.");
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await SendAsync(new { id, method, @params = parameters }, cancellationToken);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task SendAsync(object message, CancellationToken cancellationToken)
    {
        await writer.WaitAsync(cancellationToken);
        try
        {
            await process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        finally { writer.Release(); }
    }

    private async Task ReadLoopAsync()
    {
        Exception failure = new IOException("Codex app-server disconnected.");
        try
        {
            while (await process!.StandardOutput.ReadLineAsync(lifetime.Token) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int requestId)
                    && pending.TryGetValue(requestId, out var completion))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        // Do not expose arbitrary backend response text or identifiers in logs.
                        var code = error.TryGetProperty("code", out var value) ? value.ToString() : "unknown";
                        completion.TrySetException(new IOException($"Codex rejected the usage request ({code}). Check your CLI login."));
                    }
                    else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                }
                else if (root.TryGetProperty("method", out var method))
                {
                    if (root.TryGetProperty("id", out var serverId))
                        await SendAsync(new { id = serverId.Clone(), error = new { code = -32601, message = "Unsupported monitor request" } }, lifetime.Token);
                    else if (method.GetString() is "account/rateLimits/updated" or "account/updated") UsageChanged?.Invoke();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException) { failure = ex; }
        finally { foreach (var completion in pending.Values) completion.TrySetException(failure); }
    }

    private async Task DrainErrorsAsync()
    {
        try { while (await process!.StandardError.ReadLineAsync(lifetime.Token) != null) { } }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        if (process is { } child)
        {
            try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (readerTask != null) await readerTask;
            if (errorTask != null) await errorTask;
            child.Dispose();
        }
        writer.Dispose();
        lifetime.Dispose();
    }
}
