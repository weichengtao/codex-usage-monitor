using System.Text.Json;

internal static class FakeServer
{
    public static async Task RunAsync()
    {
        int reads = 0, held = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var message = document.RootElement;
            if (!message.TryGetProperty("method", out var method)) continue;
            if (method.GetString() == "initialize")
                await Reply(new { id = message.GetProperty("id").GetInt32(), result = new { } });
            else if (method.GetString() == "account/rateLimits/read")
            {
                int id = message.GetProperty("id").GetInt32();
                reads++;
                if (Environment.GetEnvironmentVariable("CODEX_MONITOR_TEST_MODE") == "transport")
                {
                    if (reads == 1) held = id;
                    else if (reads == 2)
                    {
                        await Reply(new { method = "account/rateLimits/updated", @params = new { } });
                        await Reply(new { id, result = new { sequence = 2 } });
                        await Reply(new { id = held, result = new { sequence = 1 } });
                    }
                    else return;
                }
                else
                {
                    if (reads > 1) return;
                    await Reply(new { id, result = new
                    {
                        rateLimits = new { limitId = "codex", primary = new { usedPercent = 20, windowDurationMins = 300 }, secondary = new { usedPercent = 35, windowDurationMins = 10080 } },
                        rateLimitResetCredits = new { availableCount = 2 }
                    } });
                }
            }
        }
    }
    private static async Task Reply(object value)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(value));
        await Console.Out.FlushAsync();
    }
}
