using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexUsageMonitor;

internal static class TextAlignmentCheck
{
    public static bool Run(string directory)
    {
        var measurements = new List<object>();
        double maximumError = 0;
        foreach (int size in new[] { 16, 20, 24, 32 })
        foreach (string label in Enumerable.Range(0, 101).Select(n => n.ToString()).Append("—"))
        {
            var text = IconRenderer.CreateCenterText(label, Brushes.White, size / 32.0);
            var ink = text.BuildGeometry(new Point()).Bounds;
            var oldOrigin = new Point((32 - text.Width) / 2, (32 - text.Height) / 2 - .5);
            var origin = IconRenderer.CenterTextOrigin(text);
            var oldError = new Point(oldOrigin.X + ink.X + ink.Width / 2 - 16, oldOrigin.Y + ink.Y + ink.Height / 2 - 16);
            var error = new Point(origin.X + ink.X + ink.Width / 2 - 16, origin.Y + ink.Y + ink.Height / 2 - 16);
            maximumError = Math.Max(maximumError, Math.Max(Math.Abs(error.X), Math.Abs(error.Y)));
            measurements.Add(new { label, size, oldOffsetX = oldError.X * size / 32 * 1.08, oldOffsetY = oldError.Y * size / 32 * 1.08,
                newOffsetX = error.X * size / 32 * 1.08, newOffsetY = error.Y * size / 32 * 1.08 });
        }
        File.WriteAllText(Path.Combine(directory, "text-alignment.json"), JsonSerializer.Serialize(new
        {
            passed = maximumError < .000001, cases = measurements.Count, maximumVectorError = maximumError,
            note = "Offsets in output pixels; positive Y is downward. Rasterization can add subpixel asymmetry.", measurements
        }, new JsonSerializerOptions { WriteIndented = true }));

        var labels = new[] { "0", "1", "10", "28", "47", "82", "100" };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 27, 35)), null, new Rect(0, 0, 920, 340));
            for (int row = 0; row < 2; row++)
            {
                dc.DrawText(new FormattedText(row == 0 ? "BEFORE" : "INK CENTERED", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1), new Point(16, 20 + row * 165));
                for (int column = 0; column < labels.Length; column++)
                {
                    dc.PushTransform(new TranslateTransform(30 + column * 128, 50 + row * 165));
                    dc.PushTransform(new ScaleTransform(3, 3));
                    dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(88, 220, 195)), 1), new Point(16, 16), 14, 14);
                    var text = IconRenderer.CreateCenterText(labels[column], Brushes.White, 1);
                    var origin = row == 0 ? new Point((32 - text.Width) / 2, (32 - text.Height) / 2 - .5) : IconRenderer.CenterTextOrigin(text);
                    dc.DrawText(text, origin);
                    var ink = text.BuildGeometry(origin).Bounds;
                    dc.DrawRectangle(null, new Pen(Brushes.SlateGray, .2), ink);
                    var guide = new Pen(new SolidColorBrush(Color.FromArgb(175, 255, 178, 70)), .22);
                    dc.DrawLine(guide, new Point(0, 16), new Point(32, 16));
                    dc.DrawLine(guide, new Point(16, 0), new Point(16, 32));
                    dc.Pop(); dc.Pop();
                }
            }
        }
        var bitmap = new RenderTargetBitmap(920, 340, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, "text-alignment.png")); encoder.Save(file);
        return maximumError < .000001;
    }
}
