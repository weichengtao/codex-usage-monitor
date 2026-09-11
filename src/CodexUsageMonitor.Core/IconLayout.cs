namespace CodexUsageMonitor.Core;

public sealed record IconLayout(double StartDegrees, double AvailableDegrees, double FilledDegrees,
    IReadOnlyList<double> DotDegrees, double DotRadius, string CenterText)
{
    // Angles are clockwise from 3 o'clock. Bottom is 90 degrees.
    public static IconLayout Create(double? weeklyRemaining, double? fiveHourRemaining, int? resets)
    {
        int count = Math.Clamp(resets ?? 0, 0, 1000);
        double gap = count == 0 ? 0 : Math.Min(160, 20 + count * 19);
        double spacing = count == 0 ? 19 : Math.Min(19, (gap - 20) / count);
        double available = 360 - gap;
        double start = count == 0 ? -90 : 90 + gap / 2;
        var dots = Enumerable.Range(0, count).Select(i => 90 + (i - (count - 1) / 2.0) * spacing).ToArray();
        var text = fiveHourRemaining is { } remaining && double.IsFinite(remaining)
            ? Math.Round(Math.Clamp(remaining, 0, 100), MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        return new(start, available, available * Math.Clamp(weeklyRemaining ?? 0, 0, 100) / 100,
            dots, Math.Min(1.45, 13 * spacing * Math.PI / 180 * .34), text);
    }
}
