using System.Runtime.InteropServices;
using CodexUsageMonitor;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string name)
    {
        if (!value) throw new Exception($"FAIL: {name}");
        Console.WriteLine($"PASS: {name}"); checks++;
    }
    [STAThread]
    public static void Main()
    {
        var calls = new List<(uint Operation, NativeTrayIcon.NotifyIconData Data)>();
        bool acceptAdds = true, acceptVersions = true;
        var tray = new NativeTrayIcon((operation, data) =>
        {
            calls.Add((operation, data));
            return operation == 0 ? acceptAdds : operation != 4 || acceptVersions;
        });
        using var icon = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        tray.Text = "Codex usage"; tray.Icon = icon;
        Check(calls.Count == 0, "Hidden icon does not register");
        tray.Visible = true;
        Check(calls.Select(c => c.Operation).SequenceEqual(new uint[] { 0, 4 }), "Add followed by version negotiation");
        Check(calls[0].Data.Flags == 0xa7 && calls[1].Data.Version == 4, "GUID, icon, callback, tooltip and version-4 flags");
        Check(Marshal.SizeOf<NativeTrayIcon.NotifyIconData>() == (IntPtr.Size == 8 ? 976 : 956), "Native Unicode structure matches Windows ABI");
        tray.Text = new string('x', 200);
        Check(calls[^1].Operation == 1 && calls[^1].Data.Tip.Length == 127, "Update truncates tooltip safely");
        int activated = 0, menus = 0;
        tray.Activated += () => activated++;
        tray.ContextMenuRequested += () => menus++;
        tray.HandleMessage(NativeTrayIcon.CallbackMessage, new IntPtr((1 << 16) | 0x400));
        tray.HandleMessage(NativeTrayIcon.CallbackMessage, new IntPtr((1 << 16) | 0x401));
        tray.HandleMessage(NativeTrayIcon.CallbackMessage, new IntPtr((1 << 16) | 0x202));
        tray.HandleMessage(NativeTrayIcon.CallbackMessage, new IntPtr((1 << 16) | 0x7b));
        Check(activated == 2 && menus == 1, "Version-4 mouse, keyboard and context-menu callbacks without duplicate activation");
        tray.ReturnFocus();
        Check(calls[^1].Operation == 3, "Menu dismissal restores shell focus");
        int start = calls.Count;
        tray.HandleMessage(NativeTrayIcon.TaskbarCreated, IntPtr.Zero);
        Check(calls.Skip(start).Select(c => c.Operation).SequenceEqual(new uint[] { 0, 4 }), "Explorer restart re-adds and reapplies version");
        tray.Visible = false;
        Check(calls[^1].Operation == 2, "Hiding deletes icon");
        start = calls.Count;
        tray.HandleMessage(NativeTrayIcon.TaskbarCreated, IntPtr.Zero);
        Check(calls.Count == start, "Explorer restart does not resurrect hidden icon");
        acceptAdds = false; tray.Visible = true;
        Check(calls[^1].Operation == 0, "Shell unavailable leaves registration pending");
        acceptAdds = true; tray.Text = "retry";
        Check(calls[^2].Operation == 0 && calls[^1].Operation == 4, "Next update retries registration");
        tray.Visible = false; acceptVersions = false; tray.Visible = true;
        Check(calls[^1].Operation == 2, "Failed version negotiation removes incompatible registration");
        acceptVersions = true; tray.Text = "retry version";
        tray.Dispose(); start = calls.Count; tray.Dispose();
        Check(calls[^1].Operation == 2 && calls.Count == start, "Dispose deletes exactly once");
        Check(calls.All(c => c.Data.Guid == new Guid("621dbea4-750f-4ea2-8f67-c9a6f8a1b65c") && (c.Data.Flags & 0x20) != 0), "Every operation uses the permanent GUID");
        Console.WriteLine($"{checks} tray checks passed.");
    }
}
