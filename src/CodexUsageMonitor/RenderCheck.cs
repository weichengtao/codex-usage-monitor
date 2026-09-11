using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageMonitor.Core;

namespace CodexUsageMonitor;

internal static class RenderCheck
{
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var scenarios = new (double? Week, double? Five, int? Resets, bool Stale, string Label)[]
        {
            (100, 100, 0, false, "Full / no resets"), (100, 100, 1, false, "Full / 1 reset"),
            (100, 82, 2, false, "Full / 2 resets"), (65, 47, 3, false, "65% / 3 resets"),
            (20, 9, 2, false, "20% / low 5h"), (0, 0, 1, false, "Empty / 1 reset"),
            (80, 62, null, false, "Unknown resets"), (80, 62, 2, true, "Stale data"),
            (null, null, null, true, "Unavailable"), (100, 100, 5, false, "5 resets"),
            (100, 100, 8, false, "8 resets")
        };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (int theme = 0; theme < 2; theme++)
            {
                double x = theme * 460;
                bool light = theme == 1;
                dc.DrawRectangle(new SolidColorBrush(light ? Colors.WhiteSmoke : Color.FromRgb(20, 27, 35)), null, new Rect(x, 0, 460, 900));
                Label(dc, light ? "LIGHT TASKBAR" : "DARK TASKBAR", x + 20, 15, light);
                Label(dc, "16px     20px     24px      32px       enlarged", x + 172, 42, light, 10);
                for (int row = 0; row < scenarios.Length; row++)
                {
                    var s = scenarios[row];
                    var snapshot = new UsageSnapshot(s.Five is { } f ? new(f, null) : null, s.Week is { } w ? new(w, null) : null, s.Resets, DateTimeOffset.UtcNow, "codex");
                    double y = 80 + row * 74;
                    Label(dc, s.Label, x + 20, y + 5, light, 12);
                    int column = 0;
                    foreach (int size in new[] { 16, 20, 24, 32, 64 })
                    {
                        var bitmap = IconRenderer.Render(snapshot, s.Stale, size, light);
                        dc.DrawImage(bitmap, new Rect(x + 174 + column * 48, y + (32 - size) / 2.0, size, size));
                        column++;
                    }
                }
            }
        }
        var sheet = new RenderTargetBitmap(920, 900, 96, 96, PixelFormats.Pbgra32); sheet.Render(visual);
        Save(sheet, Path.Combine(directory, "icon-preview.png"));

        var sample = new UsageSnapshot(new(82, DateTimeOffset.Now.AddHours(3)), new(65, DateTimeOffset.Now.AddDays(4)), 3, DateTimeOffset.Now, "codex");
        var details = new DetailsWindow(() => { }, new(), _ => { });
        details.Update(new(sample, null, false));
        var content = (FrameworkElement)details.Content;
        details.Content = null;
        var surface = new System.Windows.Controls.Border { Background = details.Background, Child = content, Width = 370 };
        System.Windows.Documents.TextElement.SetForeground(surface, details.Foreground);
        System.Windows.Documents.TextElement.SetFontFamily(surface, details.FontFamily);
        System.Windows.Documents.TextElement.SetFontSize(surface, details.FontSize);
        surface.Measure(new System.Windows.Size(370, 900));
        surface.Arrange(new Rect(0, 0, 370, surface.DesiredSize.Height)); surface.UpdateLayout();
        var popup = new RenderTargetBitmap(370, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        popup.Render(surface); Save(popup, Path.Combine(directory, "popup-preview.png"));
        details.Close();

        // Warm up native icon creation before measuring handle growth.
        for (int i = 0; i < 20; i++) { using var icon = IconRenderer.CreateIcon(IconRenderer.Render(sample, false, 16, false)); }
        using var process = Process.GetCurrentProcess();
        int before = GetGuiResources(process.Handle, 0) + GetGuiResources(process.Handle, 1);
        for (int i = 0; i < 1000; i++)
        {
            using var icon = IconRenderer.CreateIcon(IconRenderer.Render(sample with { Resets = i % 6 }, false, 16 + i % 3 * 8, i % 2 == 0));
        }
        GC.Collect(); GC.WaitForPendingFinalizers();
        int after = GetGuiResources(process.Handle, 0) + GetGuiResources(process.Handle, 1);
        bool passed = after <= before + 5 && TextAlignmentCheck.Run(directory);
        File.WriteAllText(Path.Combine(directory, "render-check.json"), JsonSerializer.Serialize(new { passed, iterations = 1000, handlesBefore = before, handlesAfter = after }));
        return passed ? 0 : 1;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static void Label(DrawingContext dc, string label, double x, double y, bool light, double size = 15) =>
        dc.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, light ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White, 1), new System.Windows.Point(x, y));
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
