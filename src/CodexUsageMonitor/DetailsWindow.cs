using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexUsageMonitor.Core;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Brushes = System.Windows.Media.Brushes;

namespace CodexUsageMonitor;

internal sealed class DetailsWindow : Window
{
    private readonly TextBlock five = new(), week = new(), resets = new(), status = new();
    private readonly ProgressBar fiveBar = new(), weekBar = new();
    private readonly TextBlock fiveReset = new(), weekReset = new();
    private readonly System.Windows.Controls.Image preview = new() { Width = 48, Height = 48 };

    public DetailsWindow(Action refresh, Settings settings, Action<Settings> save, bool showSettings = false)
    {
        Title = "Codex Usage Monitor";
        using (var iconStream = typeof(DetailsWindow).Assembly.GetManifestResourceStream("CodexUsageMonitor.AppIcon.png")
            ?? throw new InvalidOperationException("The app icon resource is missing."))
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconStream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        Width = 370; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(20, 27, 35)); Foreground = Brushes.White;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); FontSize = 13;
        var root = new StackPanel { Margin = new Thickness(22) };
        Content = root;
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 22) };
        DockPanel.SetDock(preview, Dock.Right); header.Children.Add(preview);
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "CODEX", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(88, 220, 195)) });
        heading.Children.Add(new TextBlock { Text = "Usage remaining", FontSize = 23, FontWeight = FontWeights.SemiBold });
        header.Children.Add(heading); root.Children.Add(header);
        AddQuota(root, "5-hour window", five, fiveBar, fiveReset);
        AddQuota(root, "Weekly window", week, weekBar, weekReset);
        resets.FontSize = 15; resets.Margin = new Thickness(0, 2, 0, 15); root.Children.Add(resets);
        status.TextWrapping = TextWrapping.Wrap; status.Foreground = Brushes.LightSlateGray; status.FontSize = 12;
        root.Children.Add(status);
        var refreshButton = new Button { Content = "Refresh now", Margin = new Thickness(0, 16, 0, 12), Padding = new Thickness(10, 7, 10, 7) };
        refreshButton.Click += (_, _) => refresh(); root.Children.Add(refreshButton);

        var expander = new Expander { Header = "Settings", IsExpanded = showSettings, Foreground = Brushes.White };
        root.Children.Add(expander);
        var options = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; expander.Content = options;
        var startup = new CheckBox { Content = "Start with Windows", IsChecked = Settings.StartupEnabled, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) };
        options.Children.Add(startup);
        startup.Click += (_, _) =>
        {
            try { Settings.StartupEnabled = startup.IsChecked == true; }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "Startup setting"); startup.IsChecked = Settings.StartupEnabled; }
        };
        options.Children.Add(new TextBlock { Text = "Codex executable (blank = auto-detect)", Margin = new Thickness(0, 0, 0, 6) });
        var path = new TextBox { Text = settings.CodexPath ?? "", Padding = new Thickness(5) }; options.Children.Add(path);
        var saveButton = new Button { Content = "Save settings", Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(8, 5, 8, 5) };
        saveButton.Click += (_, _) =>
        {
            try
            {
                var updated = new Settings(string.IsNullOrWhiteSpace(path.Text) ? null : path.Text.Trim().Trim('"'));
                AppServerClient.FindCodex(updated.CodexPath);
                save(updated); refresh();
            }
            catch (Exception ex) { System.Windows.MessageBox.Show(this, ex.Message, "Codex path"); }
        };
        options.Children.Add(saveButton);
        options.Children.Add(new TextBlock { Text = "Refreshes every 60 seconds. Amber badge = stale data.\nHollow bottom marker = reset count unavailable.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSlateGray, FontSize = 11, Margin = new Thickness(0, 12, 0, 0) });
        Loaded += (_, _) => PositionNearTray();
        SizeChanged += (_, _) => { if (IsLoaded) PositionNearTray(); };
    }

    private static void AddQuota(StackPanel root, string title, TextBlock value, ProgressBar progress, TextBlock reset)
    {
        var row = new DockPanel();
        value.FontWeight = FontWeights.SemiBold; value.FontSize = 18; DockPanel.SetDock(value, Dock.Right); row.Children.Add(value);
        row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); root.Children.Add(row);
        progress.Height = 5; progress.Maximum = 100; progress.Foreground = new SolidColorBrush(Color.FromRgb(88, 220, 195));
        progress.Background = new SolidColorBrush(Color.FromRgb(48, 60, 72)); progress.BorderThickness = new Thickness(0); progress.Margin = new Thickness(0, 8, 0, 6);
        root.Children.Add(progress);
        reset.FontSize = 11; reset.Foreground = Brushes.LightSlateGray; reset.Margin = new Thickness(0, 0, 0, 19); root.Children.Add(reset);
    }

    private void PositionNearTray()
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = (screen.WorkingArea.Right / dpi.DpiScaleX) - ActualWidth - 12;
        Top = (screen.WorkingArea.Bottom / dpi.DpiScaleY) - ActualHeight - 12;
    }

    public void Update(MonitorState state)
    {
        var snapshot = state.Snapshot;
        five.Text = Percent(snapshot?.FiveHour); week.Text = Percent(snapshot?.Weekly);
        fiveBar.Value = snapshot?.FiveHour?.Remaining ?? 0; weekBar.Value = snapshot?.Weekly?.Remaining ?? 0;
        fiveReset.Text = ResetText(snapshot?.FiveHour); weekReset.Text = ResetText(snapshot?.Weekly);
        resets.Text = snapshot?.Resets is { } count ? $"{count} reset{(count == 1 ? "" : "s")} available" : "Reset count unavailable";
        status.Text = state.Connecting ? "Connecting to Codex…" : state.Error != null ? $"Data may be stale. {state.Error}"
            : snapshot != null ? $"Updated {snapshot.FetchedAt.ToLocalTime():HH:mm:ss} · Codex account" : "Waiting for usage…";
        preview.Source = IconRenderer.Render(snapshot, state.IsStale, 96, false);
    }
    private static string Percent(UsageWindow? window) => window == null ? "—" : $"{window.Remaining:0.#}%";
    private static string ResetText(UsageWindow? window) => window?.ResetsAt is { } time ? $"Resets {time.ToLocalTime():ddd, dd MMM · HH:mm}" : "Reset time unavailable";
}
