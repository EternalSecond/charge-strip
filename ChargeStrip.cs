using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool launchEditor = args.Contains("--settings");
        bool first;
        using (var request = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ChargeStrip.OpenSettings"))
        using (var instance = new Mutex(true, @"Local\ChargeStrip", out first))
        {
            if (!first)
            {
                if (launchEditor)
                {
                    Native.AllowStripForeground();
                    request.Set();
                }
                return;
            }
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Strip(request, launchEditor));
        }
    }
}

internal sealed class Strip : Form
{
    private static readonly Color Background = Color.FromArgb(31, 35, 43);
    private static readonly Color TextColor = Color.FromArgb(240, 243, 247);
    private static readonly Color Muted = Color.FromArgb(174, 183, 196);
    private readonly ContextMenuStrip details = new ContextMenuStrip();
    private readonly ContextMenuStrip trayMenu = new ContextMenuStrip();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ToolTip tip = new ToolTip();
    private readonly Icon trayIcon;
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 12000 };
    private readonly System.Windows.Forms.Timer inputTimer = new System.Windows.Forms.Timer { Interval = 150 };
    private List<BatteryReading> rows = new List<BatteryReading>();
    private DateTime? refreshed;
    private StripSettings saved, draft;
    private SettingsWindow editor;
    private float scale = 1;
    private Point pointerDown, origin;
    private bool dragging, polling;
    private int? legacyOffset;

    internal StripSettings Current { get { return draft ?? saved; } }
    internal IList<BatteryReading> Readings { get { return rows; } }
    internal Screen PositionScreen { get; private set; }
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var p = base.CreateParams;
            p.ExStyle |= 0x80000 | 0x80 | 0x08000000; // layered, tool window, no activation
            return p;
        }
    }

    public Strip(EventWaitHandle openRequest, bool launchEditor)
    {
        Text = "Charge Strip";
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        details.ShowImageMargin = false;
        details.Renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
        details.BackColor = Background;
        details.ForeColor = TextColor;
        details.Font = new Font("Segoe UI", 10);
        trayMenu.Items.Add("Налаштування", null, (sender, args) => OpenSettings());
        trayMenu.Items.Add("Оновити", null, async (sender, args) => await RefreshReadings());
        trayMenu.Items.Add("Вийти", null, (sender, args) => Close());
        trayIcon = CreateTrayIcon();
        tray.Icon = trayIcon;
        tray.Text = Text;
        tray.ContextMenuStrip = trayMenu;
        tray.Visible = true;
        tray.DoubleClick += (sender, args) => OpenSettings();
        tip.InitialDelay = 350;
        tip.SetToolTip(this, Text);

        saved = StripSettings.Load();
        bool first = saved == null;
        if (first)
        {
            saved = new StripSettings();
            try
            {
                int offset;
                if (int.TryParse(File.ReadAllText(Path.Combine(StripSettings.Folder, "position.txt")), out offset) && offset >= 0)
                    legacyOffset = offset;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        Shown += async (sender, args) =>
        {
            ApplySettings();
            timer.Start();
            inputTimer.Start();
            if (launchEditor) OpenSettings();
            await RefreshReadings();
        };
        inputTimer.Tick += (sender, args) =>
        {
            if (openRequest.WaitOne(0)) OpenSettings();
            if (Visible && !details.Visible && !trayMenu.Visible) Native.KeepAboveTaskbar(Handle, Bounds);
        };
        timer.Tick += async (sender, args) => { if (!Capture) ApplySettings(); await RefreshReadings(); };
    }

    private static Icon CreateTrayIcon()
    {
        using (var bitmap = new Bitmap(16, 16))
        using (var g = Graphics.FromImage(bitmap))
        using (var outline = new Pen(Color.FromArgb(240, 243, 247)))
        using (var charge = new SolidBrush(Color.FromArgb(155, 225, 194)))
        {
            g.DrawRectangle(outline, 1, 4, 12, 8);
            g.DrawLine(outline, 14, 6, 14, 10);
            g.FillRectangle(charge, 3, 6, 7, 5);
            IntPtr handle = bitmap.GetHicon();
            try { using (var borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone(); }
            finally { Native.DestroyIcon(handle); }
        }
    }

    private static Point AutomaticPosition(Screen screen, Size size, int? oldOffset)
    {
        IntPtr shell = Native.FindWindow("Shell_TrayWnd", null);
        Native.Rect bar, notification;
        if (screen.Primary && shell != IntPtr.Zero && Native.GetWindowRect(shell, out bar) && bar.Right - bar.Left > bar.Bottom - bar.Top)
        {
            IntPtr trayWindow = Native.FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
            int right = trayWindow != IntPtr.Zero && Native.GetWindowRect(trayWindow, out notification) && notification.Left > bar.Left
                ? notification.Left - 6 : bar.Right - 240;
            return new Point(right - Math.Max(oldOffset ?? 0, size.Width), bar.Top + (bar.Bottom - bar.Top - size.Height) / 2);
        }
        Rectangle bounds = screen.Bounds;
        int taskbarHeight = Math.Max(0, bounds.Bottom - screen.WorkingArea.Bottom);
        return new Point(bounds.Right - 240 - size.Width,
            bounds.Bottom - Math.Max(taskbarHeight, size.Height) + (Math.Max(taskbarHeight, size.Height) - size.Height) / 2);
    }

    private static Size SizeFor(Screen screen, int count, StripSettings settings, out float factor)
    {
        factor = settings.Zoom / 100f * Native.ScreenScale(screen.Bounds);
        Size size = StripArtwork.Measure(count, settings, factor);
        if (size.Width > screen.Bounds.Width || size.Height > screen.Bounds.Height)
        {
            factor *= Math.Min(screen.Bounds.Width / (float)size.Width, screen.Bounds.Height / (float)size.Height);
            size = StripArtwork.Measure(count, settings, factor);
        }
        return size;
    }

    internal void ApplySettings()
    {
        if (IsDisposed || Capture) return;
        ApplyPosition();
        if (editor != null) editor.SyncPosition();
    }

    private void ApplyPosition()
    {
        var settings = Current;
        Screen requested = Screen.AllScreens.FirstOrDefault(screen => screen.DeviceName == settings.Monitor);
        Screen screenToUse = requested ?? Screen.PrimaryScreen;
        Size size = SizeFor(screenToUse, rows.Count, settings, out scale);
        Point target = settings.Automatic || requested == null
            ? AutomaticPosition(screenToUse, size, settings == saved ? legacyOffset : null)
            : new Point(screenToUse.Bounds.Left + settings.X, screenToUse.Bounds.Top + settings.Y);
        PositionScreen = screenToUse;
        Render(KeepVisible(target, size, screenToUse.Bounds), size);
    }

    private static Point KeepVisible(Point point, Size size, Rectangle screen)
    {
        return new Point(StripSettings.Clamp(point.X, screen.Left, screen.Right - size.Width),
            StripSettings.Clamp(point.Y, screen.Top, screen.Bottom - size.Height));
    }

    private void Render(Point position, Size size)
    {
        Bounds = new Rectangle(position, size);
        using (var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb))
        {
            using (var graphics = Graphics.FromImage(bitmap)) StripArtwork.Draw(graphics, rows, Current, scale);
            Native.Present(Handle, bitmap, position, size);
            if (!details.Visible && !trayMenu.Visible) Native.KeepAboveTaskbar(Handle, Bounds);
        }
    }

    internal void OpenSettings()
    {
        details.Close();
        if (editor == null)
        {
            draft = saved.Copy();
            if (legacyOffset.HasValue && saved.Automatic && PositionScreen != null)
                StoreEffectivePosition(draft);
            editor = new SettingsWindow(this);
            editor.FormClosed += (sender, args) =>
            {
                editor = null;
                draft = null;
                ApplySettings();
            };
            editor.Show();
        }
        if (editor.WindowState == FormWindowState.Minimized) editor.WindowState = FormWindowState.Normal;
        editor.BringToFront();
        editor.Activate();
    }

    internal void ResetPosition()
    {
        draft.Automatic = true;
        draft.Monitor = "";
        ApplySettings();
    }

    internal void ResetSettings()
    {
        draft = new StripSettings();
        ApplySettings();
    }

    internal void SetPosition(string monitor, int x, int y)
    {
        draft.Monitor = monitor;
        draft.Automatic = false;
        draft.X = x;
        draft.Y = y;
        ApplyPosition();
    }

    internal bool Commit(bool startWithWindows)
    {
        var candidate = draft.Copy();
        if (!candidate.Automatic) StoreEffectivePosition(candidate);

        bool previousStartup;
        try
        {
            previousStartup = StartupRegistration.Enabled;
            if (previousStartup != startWithWindows) StartupRegistration.SetEnabled(startWithWindows);
        }
        catch (Exception error)
        {
            if (!StartupRegistration.IsAccessError(error)) throw;
            MessageBox.Show(editor, "Не вдалося змінити автозапуск Windows.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        if (!Save(candidate))
        {
            try
            {
                if (StartupRegistration.Enabled != previousStartup)
                    StartupRegistration.SetEnabled(previousStartup);
            }
            catch (Exception error)
            {
                if (!StartupRegistration.IsAccessError(error)) throw;
                MessageBox.Show(editor, "Не вдалося відновити попередній стан автозапуску.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return false;
        }

        saved = candidate;
        legacyOffset = null;
        return true;
    }

    private void StoreEffectivePosition(StripSettings settings)
    {
        settings.Monitor = PositionScreen.DeviceName;
        settings.Automatic = false;
        settings.X = Left - PositionScreen.Bounds.Left;
        settings.Y = Top - PositionScreen.Bounds.Top;
    }

    private bool Save(StripSettings settings)
    {
        try { settings.Save(); return true; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        MessageBox.Show(editor != null ? (IWin32Window)editor : this, "Не вдалося зберегти налаштування.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        return false;
    }

    private async Task RefreshReadings()
    {
        if (polling) return;
        polling = true;
        try
        {
            rows = await Task.Run(() => Batteries.Read());
            if (IsDisposed) return;
            refreshed = DateTime.Now;
            ApplySettings();
            if (details.Visible) FillDetails();
        }
        finally { polling = false; }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == 0x007E && IsHandleCreated && PositionScreen != null)
            BeginInvoke((Action)ApplySettings); // WM_DISPLAYCHANGE
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        pointerDown = Native.MouseMessagePosition();
        origin = Location;
        dragging = false;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left)
        {
            int cell = (int)Math.Floor((e.X / scale - 13) / (51 + Current.Gap));
            string title = cell >= 0 && cell < Math.Min(rows.Count, StripArtwork.MaxVisible) ? rows[cell].Name : Text;
            if (tip.GetToolTip(this) != title) tip.SetToolTip(this, title);
            return;
        }

        Point pointer = Native.MouseMessagePosition();
        int dx = pointer.X - pointerDown.X, dy = pointer.Y - pointerDown.Y;
        if (!dragging && Math.Max(Math.Abs(dx), Math.Abs(dy)) < 4) return;
        dragging = true;
        PositionScreen = Screen.FromPoint(pointer);
        Size size = SizeFor(PositionScreen, rows.Count, Current, out scale);
        Render(KeepVisible(new Point(origin.X + dx, origin.Y + dy), size, PositionScreen.Bounds), size);
        if (draft != null)
        {
            StoreEffectivePosition(draft);
            if (editor != null) editor.SyncPosition();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { OpenSettings(); return; }
        if (e.Button != MouseButtons.Left || !Capture) return;
        Capture = false;
        if (!dragging) { OpenDetails(); return; }
        if (draft != null) return;
        var candidate = saved.Copy();
        StoreEffectivePosition(candidate);
        if (Save(candidate)) { saved = candidate; legacyOffset = null; }
        ApplySettings();
    }

    private void OpenDetails()
    {
        FillDetails();
        details.Show(this, Point.Empty, ToolStripDropDownDirection.AboveRight);
    }

    private void FillDetails()
    {
        while (details.Items.Count > 0)
        {
            var item = details.Items[0];
            details.Items.RemoveAt(0);
            item.Dispose();
        }
        if (rows.Count == 0)
            details.Items.Add(new ToolStripLabel(refreshed.HasValue ? "Немає доступних показників заряду" : "Зчитування показників…"));
        foreach (BatteryReading row in rows)
        {
            string status = row.Percent.HasValue ? row.Level + " · " + row.Status : row.Status;
            details.Items.Add(new ToolStripLabel(row.Name + Environment.NewLine + status) { TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(3, 5, 12, 5) });
        }
        if (refreshed.HasValue)
            details.Items.Add(new ToolStripLabel("Оновлено о " + refreshed.Value.ToString("HH:mm:ss")) { ForeColor = Muted });
        details.Items.Add(new ToolStripSeparator());
        details.Items.Add("Оновити", null, async (sender, args) => await RefreshReadings());
        details.Items.Add("Налаштування", null, (sender, args) => OpenSettings());
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        private static readonly Color Highlight = Color.FromArgb(56, 67, 83);
        private static readonly Color Border = Color.FromArgb(65, 74, 88);
        public override Color MenuItemSelected { get { return Highlight; } }
        public override Color MenuItemBorder { get { return Highlight; } }
        public override Color MenuBorder { get { return Border; } }
        public override Color SeparatorDark { get { return Border; } }
        public override Color SeparatorLight { get { return Border; } }
        public override Color ToolStripDropDownBackground { get { return Background; } }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (editor != null) editor.Dispose();
            timer.Dispose();
            inputTimer.Dispose();
            tip.Dispose();
            details.Dispose();
            tray.Visible = false;
            tray.Dispose();
            trayMenu.Dispose();
            trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blend { public byte Operation, Flags, Alpha, Format; }

    internal static Point MouseMessagePosition()
    {
        uint pos = GetMessagePos();
        return new Point(unchecked((short)pos), unchecked((short)(pos >> 16)));
    }

    internal static void AllowStripForeground()
    {
        IntPtr strip = FindWindow(null, "Charge Strip");
        uint pid;
        if (strip != IntPtr.Zero && GetWindowThreadProcessId(strip, out pid) != 0) AllowSetForegroundWindow(pid);
    }

    internal static void KeepAboveTaskbar(IntPtr strip, Rectangle bounds)
    {
        IntPtr hit = WindowFromPoint(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        if (hit == IntPtr.Zero || hit == strip) return;
        IntPtr root = GetAncestor(hit, 2);
        var name = new StringBuilder(64);
        GetClassName(root, name, name.Capacity);
        if (name.ToString() == "Shell_TrayWnd" || name.ToString() == "Shell_SecondaryTrayWnd")
            SetWindowPos(strip, new IntPtr(-1), 0, 0, 0, 0, 0x213); // no move, resize, activation, or owner reorder
    }

    internal static float ScreenScale(Rectangle bounds)
    {
        IntPtr monitor = MonitorFromPoint(new Point(bounds.Left, bounds.Top), 2);
        uint x, y;
        return GetDpiForMonitor(monitor, 0, out x, out y) == 0 && x > 0 ? x / 96f : 1f;
    }

    internal static void Present(IntPtr window, Bitmap bitmap, Point position, Size size)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr pixels = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr previous = SelectObject(memory, pixels);
        try
        {
            Point origin = Point.Empty;
            var blend = new Blend { Alpha = 255, Format = 1 };
            UpdateLayeredWindow(window, screen, ref position, ref size, memory, ref origin, 0, ref blend, 2);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteObject(pixels);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string name, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string title);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern uint GetMessagePos();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr dst, ref Point position, ref Size size, IntPtr src, ref Point origin, uint key, ref Blend blend, uint flags);
}
