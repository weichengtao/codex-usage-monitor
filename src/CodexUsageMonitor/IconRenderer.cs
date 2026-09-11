using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageMonitor.Core;
using Microsoft.Win32;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace CodexUsageMonitor;

internal static class IconRenderer
{
    public static bool IsLightTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public static int TrayPixelSize()
    {
        uint dpi = GetDpiForWindow(FindWindow("Shell_TrayWnd", null));
        return Math.Clamp((int)Math.Round(16 * (dpi == 0 ? 96 : dpi) / 96.0), 16, 64);
    }

    public static BitmapSource Render(UsageSnapshot? snapshot, bool stale, int size, bool light)
    {
        var layout = IconLayout.Create(snapshot?.Weekly?.Remaining, snapshot?.FiveHour?.Remaining, snapshot?.Resets);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
            // Fill more of the shell's fixed icon slot, keeping a small antialiasing margin.
            dc.PushTransform(new ScaleTransform(1.08, 1.08, 16, 16));
            var foreground = Brush(light ? "#172B3A" : "#F1F7FB");
            var track = Brush(light ? "#C2CBD1" : "#4C5662");
            var quota = Brush(stale ? (light ? "#7C8790" : "#8B97A2") : snapshot?.Weekly?.Remaining switch
            {
                <= 10 => "#F06464", <= 25 => light ? "#A56600" : "#F4BD61", _ => light ? "#007C72" : "#58DCC3"
            });
            DrawArc(dc, track, 2.4, layout.StartDegrees, layout.AvailableDegrees);
            DrawArc(dc, quota, 2.4, layout.StartDegrees, layout.FilledDegrees);
            foreach (var angle in layout.DotDegrees)
                dc.DrawEllipse(stale ? quota : foreground, null, OnRing(angle), layout.DotRadius, layout.DotRadius);

            var text = CreateCenterText(layout.CenterText, stale ? quota : foreground, size / 32.0);
            dc.DrawText(text, CenterTextOrigin(text));
            if (stale)
                dc.DrawEllipse(Brush("#F4BD61"), new Pen(Brush(light ? "#FFFFFF" : "#20252B"), 0.7), new Point(26, 5), 2.3, 2.3);
            else if (snapshot?.Resets == null)
                dc.DrawEllipse(null, new Pen(foreground, 1), new Point(16, 29), 1.65, 1.65);
            dc.Pop();
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    internal static FormattedText CreateCenterText(string label, System.Windows.Media.Brush brush, double pixelsPerDip) =>
        new(label, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed),
            label.Length == 3 ? 13 : 16, brush, pixelsPerDip);

    internal static Point CenterTextOrigin(FormattedText text)
    {
        // Font line boxes include side bearings and unused ascent/descent space. Center the ink instead.
        var ink = text.BuildGeometry(new Point(0, 0)).Bounds;
        return ink.IsEmpty ? new Point((32 - text.Width) / 2, (32 - text.Height) / 2)
            : new Point(16 - ink.X - ink.Width / 2, 16 - ink.Y - ink.Height / 2);
    }

    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static Point OnRing(double degrees) => new(16 + 13 * Math.Cos(degrees * Math.PI / 180), 16 + 13 * Math.Sin(degrees * Math.PI / 180));
    private static void DrawArc(DrawingContext dc, System.Windows.Media.Brush brush, double width, double start, double sweep)
    {
        if (sweep <= 0) return;
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (sweep >= 359.99) { dc.DrawEllipse(null, pen, new Point(16, 16), 13, 13); return; }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(OnRing(start), false, false);
            context.ArcTo(OnRing(start + sweep), new System.Windows.Size(13, 13), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        dc.DrawGeometry(null, pen, geometry);
    }

    public static System.Drawing.Icon CreateIcon(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        stream.Position = 0;
        using var bitmap = new System.Drawing.Bitmap(stream);
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)borrowed.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? windowName);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
}
