using System.Text.Json;

namespace CodexUsageMonitor.Core;

public sealed record UsageWindow(double Remaining, DateTimeOffset? ResetsAt);

public sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? Weekly, int? Resets,
    DateTimeOffset FetchedAt, string Bucket)
{
    public static UsageSnapshot Parse(JsonElement result, DateTimeOffset now)
    {
        JsonElement bucket;
        // Never silently substitute another model's quota for the main Codex bucket.
        if (result.TryGetProperty("rateLimitsByLimitId", out var buckets) && buckets.ValueKind == JsonValueKind.Object
            && buckets.TryGetProperty("codex", out var codex) && codex.ValueKind == JsonValueKind.Object)
            bucket = codex;
        else if (result.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object
            && (!legacy.TryGetProperty("limitId", out var id) || id.ValueKind == JsonValueKind.Null || id.GetString() == "codex"))
            bucket = legacy;
        else
            throw new InvalidDataException("The account has no main Codex usage bucket.");

        UsageWindow? fiveHour = null, weekly = null;
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!bucket.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
                || !window.TryGetProperty("windowDurationMins", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out var minutes)
                || !window.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var percent)
                || !double.IsFinite(percent)) continue;
            DateTimeOffset? resetsAt = null;
            if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind == JsonValueKind.Number && reset.TryGetInt64(out var unix))
            {
                try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(unix); }
                catch (ArgumentOutOfRangeException) { /* Keep usable quota even if timestamp is invalid. */ }
            }
            var value = new UsageWindow(Math.Clamp(100 - percent, 0, 100), resetsAt);
            if (minutes == 300) fiveHour = value;
            if (minutes == 10080) weekly = value;
        }
        int? resets = null;
        if (result.TryGetProperty("rateLimitResetCredits", out var credits) && credits.ValueKind == JsonValueKind.Object
            && credits.TryGetProperty("availableCount", out var count) && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out var number) && number is >= 0 and <= 1000)
            resets = number;
        return new(fiveHour, weekly, resets, now, "codex");
    }
}
