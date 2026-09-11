using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace CodexUsageMonitor;

internal sealed class NativeTrayIcon : IDisposable
{
    // Permanent application identity. Never generate this at runtime or change it between releases.
    internal static readonly Guid IconGuid = new("621dbea4-750f-4ea2-8f67-c9a6f8a1b65c");
    internal const int CallbackMessage = 0x8001;
    internal static readonly int TaskbarCreated = (int)RegisterWindowMessage("TaskbarCreated");
    private const uint Add = 0, Modify = 1, Delete = 2, SetFocus = 3, SetVersion = 4;
    private const uint GuidFlag = 0x20, DisplayFlags = GuidFlag | 1 | 2 | 4 | 0x80;
    private readonly HwndSource window;
    private readonly Func<uint, NotifyIconData, bool> notify;
    private System.Drawing.Icon? icon;
    private string text = "";
    private bool visible, registered, disposed;
    public event Action? Activated;
    public event Action? ContextMenuRequested;

    public NativeTrayIcon(Func<uint, NotifyIconData, bool>? notify = null)
    {
        this.notify = notify ?? ((uint operation, NotifyIconData data) => ShellNotifyIcon(operation, ref data));
        // A hidden top-level window receives Explorer's broadcast; a message-only window would not.
        window = new HwndSource(new HwndSourceParameters("Codex Usage Monitor tray callback")
        {
            Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80
        });
        window.AddHook(WindowProc);
    }

    public System.Drawing.Icon? Icon { set { icon = value; Refresh(); } }
    public string Text { set { text = value.Length > 127 ? value[..127] : value; Refresh(); } }
    public bool Visible
    {
        set
        {
            visible = value;
            if (visible) Refresh();
            else Remove();
        }
    }

    private NotifyIconData Data(uint flags = GuidFlag) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window.Handle, Id = 1,
        Flags = flags, Callback = CallbackMessage, Icon = icon?.Handle ?? IntPtr.Zero,
        Tip = text, Info = "", InfoTitle = "", Guid = IconGuid
    };

    private void Refresh()
    {
        if (disposed || !visible || icon == null) return;
        var data = Data(DisplayFlags);
        if (registered && notify(Modify, data)) return;
        registered = notify(Add, data);
        if (!registered) return; // Retry on the next five-second tray update if Explorer is not ready.
        var version = Data(); version.Version = 4;
        if (!notify(SetVersion, version)) Remove(); // Never interpret legacy events as version 4.
    }

    private void Remove()
    {
        if (registered) notify(Delete, Data());
        registered = false;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = HandleMessage(message, lParam);
        return IntPtr.Zero;
    }

    internal bool HandleMessage(int message, IntPtr lParam)
    {
        if (disposed) return false;
        if (message == TaskbarCreated)
        {
            registered = false;
            Refresh();
            return true;
        }
        if (message != CallbackMessage || !visible || !registered) return false;
        // Version 4 packs the notification in LOWORD and the icon ID in HIWORD of lParam.
        switch ((int)(lParam.ToInt64() & 0xffff))
        {
            case 0x400: // NIN_SELECT (mouse)
            case 0x401: // NIN_KEYSELECT (keyboard)
                Activated?.Invoke();
                break;
            case 0x7b: // WM_CONTEXTMENU
                ContextMenuRequested?.Invoke();
                break;
        }
        return true;
    }

    public void ShowContextMenu(Forms.ContextMenuStrip menu)
    {
        // WM_CONTEXTMENU's wParam is undefined under version 4; query the icon rectangle instead.
        var identifier = new IconIdentifier { Size = (uint)Marshal.SizeOf<IconIdentifier>(), Guid = IconGuid };
        var anchor = Forms.Cursor.Position;
        if (ShellNotifyIconGetRect(ref identifier, out var rect) == 0)
            anchor = new System.Drawing.Point(rect.Left, rect.Top);
        SetForegroundWindow(window.Handle);
        menu.Show(anchor, Forms.ToolStripDropDownDirection.AboveLeft);
    }

    public void ReturnFocus()
    {
        if (!disposed && registered) notify(SetFocus, Data());
        if (!disposed) PostMessage(window.Handle, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (disposed) return;
        Remove(); disposed = true;
        window.RemoveHook(WindowProc);
        window.Dispose();
        // Icon ownership remains with the renderer's caller.
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconIdentifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconGetRect")]
    private static extern int ShellNotifyIconGetRect(ref IconIdentifier identifier, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
