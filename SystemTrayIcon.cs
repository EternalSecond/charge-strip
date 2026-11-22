using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class SystemTrayIcon : NativeWindow, IDisposable
{
    private readonly uint iconId;
    private const uint IconMessage = 0x8001;
    private const uint IconFlags = 0x0001 | 0x0002 | 0x0004 | 0x0080; // message, icon, tooltip, show tooltip
    private const int Select = 0x400, KeySelect = 0x401, ContextMenu = 0x007b;
    private const int LeftUp = 0x0202, RightUp = 0x0205;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private Icon icon;
    private string tooltip = "Charge Strip";
    private bool registered, version4;
    public event Action Activated;
    public event Action MenuRequested;

    internal SystemTrayIcon(uint id = 1, string caption = "Charge Strip notification icon")
    {
        iconId = id;
        CreateHandle(new CreateParams { Caption = caption });
    }

    internal void Update(Icon next, string text)
    {
        bool changed = icon != next || tooltip != text;
        icon = next;
        tooltip = text;
        if (registered && changed)
        {
            var data = MakeData();
            registered = Shell_NotifyIcon(1, ref data);
        }
        if (!registered) Register();
    }

    private void Register()
    {
        if (icon == null) return;
        var data = MakeData();
        registered = Shell_NotifyIcon(0, ref data);
        if (!registered) return;
        data.Version = 4;
        version4 = Shell_NotifyIcon(4, ref data);
    }

    internal Rectangle Bounds
    {
        get
        {
            var identifier = new Identifier { Size = (uint)Marshal.SizeOf(typeof(Identifier)), Window = Handle, Id = iconId };
            Rect rect;
            return Shell_NotifyIconGetRect(ref identifier, out rect) == 0
                ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom) : Rectangle.Empty;
        }
    }

    internal Point PopupAnchor()
    {
        var bounds = Bounds;
        return bounds.IsEmpty ? Cursor.Position : new Point(bounds.Right, bounds.Top - 5);
    }

    internal void PrepareMenu() { SetForegroundWindow(Handle); }

    protected override void WndProc(ref Message message)
    {
        if ((uint)message.Msg == taskbarCreated)
        {
            registered = false;
            Register();
        }
        else if ((uint)message.Msg == IconMessage)
        {
            int notification = (int)(message.LParam.ToInt64() & 0xffff);
            if (version4 ? notification == Select || notification == KeySelect : notification == LeftUp)
            {
                if (Activated != null) Activated();
                return;
            }
            if (version4 ? notification == ContextMenu : notification == RightUp)
            {
                if (MenuRequested != null) MenuRequested();
                return;
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        if (registered)
        {
            var data = MakeData();
            Shell_NotifyIcon(2, ref data);
        }
        DestroyHandle();
    }

    private Data MakeData()
    {
        return new Data
        {
            Size = (uint)Marshal.SizeOf(typeof(Data)), Window = Handle, Id = iconId,
            Flags = IconFlags, Callback = IconMessage, Icon = icon == null ? IntPtr.Zero : icon.Handle,
            Tip = tooltip.Length < 128 ? tooltip : tooltip.Substring(0, 127)
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Data
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
    private struct Identifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref Data data);
    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref Identifier identifier, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
