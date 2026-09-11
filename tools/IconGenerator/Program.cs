using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexUsageMonitor;
using CodexUsageMonitor.Core;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Provide the asset output directory.");
        Directory.CreateDirectory(args[0]);
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var frames = sizes.Select(size => Render(size)).ToArray();
        using (var file = File.Create(Path.Combine(args[0], "AppIcon.ico")))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            uint offset = (uint)(6 + sizes.Length * 16);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length); writer.Write(offset);
                offset += (uint)frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
        File.WriteAllBytes(Path.Combine(args[0], "AppIcon.png"), Render(512));
        // Decode every ICO frame to verify directory entries and PNG payloads.
        using var check = File.OpenRead(Path.Combine(args[0], "AppIcon.ico"));
        var decoder = new IconBitmapDecoder(check, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (!decoder.Frames.Select(f => f.PixelWidth).SequenceEqual(sizes)) throw new InvalidDataException("ICO sizes do not match.");
        Console.WriteLine($"Created app icon: 88 / 88 / 3; verified {sizes.Length} ICO sizes and a 512px preview.");
    }

    private static byte[] Render(int size)
    {
        var snapshot = new UsageSnapshot(new(88, null), new(88, null), 3, DateTimeOffset.UnixEpoch, "codex");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // A dark tile keeps white digits and reset dots legible on any desktop background.
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(20, 27, 35)), null,
                new Rect(0, 0, size, size), size * .22, size * .22);
            double padding = size * .07;
            dc.DrawImage(IconRenderer.Render(snapshot, false, size, false),
                new Rect(padding, padding, size - padding * 2, size - padding * 2));
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
    }
}
