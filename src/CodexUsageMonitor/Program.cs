using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CodexUsageMonitor;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--probe") return ProbeAsync(args[1]).GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--render-check") return RenderCheck.Run(args[1]);
        if (args.Contains("--exit")) return Signal("Exit");
        using var singleton = new Mutex(true, @"Local\CodexUsageMonitor", out bool isFirst);
        if (!isFirst) return Signal("Show");
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexUsageMonitor.Show");
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CodexUsageMonitor.Exit");
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        using var tray = new TrayApplication(app);
        var showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => app.Dispatcher.BeginInvoke(() => tray.ShowDetails()), null, Timeout.Infinite, false);
        var exitWait = ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) => app.Dispatcher.BeginInvoke(async () => await tray.ExitAsync()), null, Timeout.Infinite, false);
        if (args.Contains("--details")) tray.ShowDetails();
        try { app.Run(); }
        finally { showWait.Unregister(null); exitWait.Unregister(null); }
        return 0;
    }

    private static int Signal(string action)
    {
        try { using var signal = EventWaitHandle.OpenExisting($@"Local\CodexUsageMonitor.{action}"); signal.Set(); return 0; }
        catch (WaitHandleCannotBeOpenedException) { return 0; }
    }

    private static async Task<int> ProbeAsync(string output)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            await using var client = new AppServerClient();
            await client.StartAsync(Settings.Load().CodexPath, timeout.Token);
            var response = await client.ReadUsageAsync(timeout.Token);
            var snapshot = Core.UsageSnapshot.Parse(response, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { error = ex.Message }));
            return 1;
        }
    }
}

internal sealed class TrayApplication : IDisposable
{
    private readonly System.Windows.Application app;
    private readonly NativeTrayIcon tray;
    private readonly System.Windows.Forms.ContextMenuStrip menu;
    private readonly UsageMonitor monitor;
    private readonly DispatcherTimer timer;
    private Settings settings = Settings.Load();
    private DetailsWindow? details;
    private System.Drawing.Icon? icon;
    private string? lastRenderKey;
    private bool exiting;

    public TrayApplication(System.Windows.Application app)
    {
        this.app = app;
        monitor = new UsageMonitor(() => settings.CodexPath);
        tray = new NativeTrayIcon { Text = "Codex Usage Monitor · Connecting…" };
        menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Usage details", null, (_, _) => ShowDetails());
        menu.Items.Add("Refresh now", null, (_, _) => monitor.Refresh());
        menu.Items.Add("Settings", null, (_, _) => ShowDetails(true));
        var startup = new System.Windows.Forms.ToolStripMenuItem("Start with Windows") { Checked = Settings.StartupEnabled };
        startup.Click += (_, _) =>
        {
            try { Settings.StartupEnabled = !Settings.StartupEnabled; startup.Checked = Settings.StartupEnabled; }
            catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "Startup setting"); }
        };
        menu.Opening += (_, _) => startup.Checked = Settings.StartupEnabled;
        menu.Items.Add(startup);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, async (_, _) => await ExitAsync());
        tray.ContextMenuRequested += () => tray.ShowContextMenu(menu);
        tray.Activated += () => ShowDetails();
        menu.Closed += (_, _) => tray.ReturnFocus();
        monitor.Changed += OnChanged;
        SystemEvents.PowerModeChanged += OnPowerMode;
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => Update(monitor.State);
        Update(monitor.State);
        tray.Visible = true;
        timer.Start();
        monitor.Start();
    }

    private void OnPowerMode(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) monitor.Refresh();
    }
    private void OnChanged(MonitorState state)
    {
        if (!exiting) app.Dispatcher.BeginInvoke(() => { if (!exiting) Update(state); });
    }
    private void Update(MonitorState state)
    {
        int size = IconRenderer.TrayPixelSize();
        bool light = IconRenderer.IsLightTaskbar();
        var snapshot = state.Snapshot;
        string key = $"{size}|{light}|{state.IsStale}|{snapshot?.FiveHour?.Remaining}|{snapshot?.Weekly?.Remaining}|{snapshot?.Resets}";
        if (key != lastRenderKey)
        {
            var next = IconRenderer.CreateIcon(IconRenderer.Render(snapshot, state.IsStale, size, light));
            tray.Icon = next;
            icon?.Dispose(); icon = next;
            lastRenderKey = key;
        }
        string five = snapshot?.FiveHour is { } f ? $"{f.Remaining:0.#}%" : "?";
        string week = snapshot?.Weekly is { } w ? $"{w.Remaining:0.#}%" : "?";
        string resets = snapshot?.Resets?.ToString() ?? "?";
        string time = snapshot?.FetchedAt.ToLocalTime().ToString("HH:mm:ss") ?? "never";
        string resetTime = snapshot?.FiveHour?.ResetsAt?.ToLocalTime().ToString("HH:mm") ?? "?";
        string weeklyTime = snapshot?.Weekly?.ResetsAt?.ToLocalTime().ToString("ddd HH:mm") ?? "?";
        string tooltip = $"{(state.IsStale ? "[STALE] " : "")}Codex · remaining\n5h {five} ↻{resetTime} | Week {week} ↻{weeklyTime}\nResets {resets} · Updated {time}";
        tray.Text = tooltip.Length <= 127 ? tooltip : tooltip[..127];
        details?.Update(state);
    }
    public void ShowDetails(bool showSettings = false)
    {
        if (details != null && showSettings) { details.Close(); details = null; }
        if (details == null)
        {
            details = new DetailsWindow(monitor.Refresh, settings, value => { value.Save(); settings = value; }, showSettings);
            details.Closed += (_, _) => details = null;
            details.Update(monitor.State);
            details.Show();
        }
        details.Activate();
    }
    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); tray.Visible = false;
        await monitor.DisposeAsync();
        app.Shutdown();
    }
    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerMode;
        monitor.Changed -= OnChanged;
        timer.Stop(); details?.Close();
        tray.Visible = false; menu.Dispose(); tray.Dispose(); icon?.Dispose();
        if (!exiting) monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
