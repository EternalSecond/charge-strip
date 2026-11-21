using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed class LauncherButton : Form
{
    private readonly Strip owner;
    private readonly ToolTip tip = new ToolTip();
    private Rectangle bar;
    private int rightEdge;
    private Point start;
    private int originalLeft;
    private bool dragged;

    internal LauncherButton(Strip owner)
    {
        this.owner = owner;
        Text = "Charge";
        AccessibleName = Text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.Manual;
        Cursor = Cursors.Hand;
        tip.SetToolTip(this, Text);
    }

    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x80 | 0x08000000; // Tool window and no activation.
            return parameters;
        }
    }

    private int Scale(int units) { return (int)Math.Round(units * DeviceDpi / 96.0); }

    internal bool UpdatePlacement()
    {
        Rectangle area;
        int edge;
        if (!owner.Current.Launcher || !LauncherNative.Taskbar(out area, out edge))
        {
            if (Visible) Hide();
            return false;
        }

        if (!Capture)
        {
            bar = area;
            rightEdge = edge;
            int width = Scale(82), height = Math.Min(bar.Height - 4, Scale(32));
            int left = rightEdge - width - (owner.Current.LauncherOffset ?? 0);
            var bounds = new Rectangle(ClampLeft(left, width), bar.Top + (bar.Height - height) / 2, width, height);
            if (Bounds != bounds)
            {
                bool resized = Size != bounds.Size;
                Bounds = bounds;
                if (resized)
                {
                    Region previous = Region;
                    using (var path = Outline(new Rectangle(0, 0, Width, Height), Height))
                        Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }
        }

        if (!Visible) Show();
        LauncherNative.KeepAboveTaskbar(Handle, Bounds);
        return true;
    }

    private int ClampLeft(int left, int width)
    {
        return StripSettings.Clamp(left, bar.Left, Math.Max(bar.Left, rightEdge - width));
    }

    internal void RefreshAppearance()
    {
        var readings = owner.Readings;
        var reading = readings.Count > 0 ? readings[0] : null;
        tip.SetToolTip(this, reading == null ? "Charge Strip" : owner.Translate(reading.Name) + " · " + reading.Level);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color text = StripSettings.ColorOf(owner.Current.Text);
        Color surface = StripSettings.ColorOf(owner.Current.Background);
        var readings = owner.Readings;
        var reading = readings.Count > 0 ? readings[0] : null;
        Color accent = StripSettings.ColorOf(reading != null && reading.Percent <= 25 ? owner.Current.Low : owner.Current.Icons);
        var area = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = Outline(area, area.Height))
        using (var fill = new LinearGradientBrush(area, ControlPaint.Light(surface, .13f), surface, 90f))
        using (var border = new Pen(Color.FromArgb(100, text)))
        using (var icon = new Font("Segoe MDL2 Assets", 10, FontStyle.Regular))
        using (var label = new Font("Segoe UI", 9, FontStyle.Bold))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
            const TextFormatFlags alignment = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, "\uE850", icon, new Rectangle(Scale(11), 0, Scale(17), Height), accent, alignment);
            TextRenderer.DrawText(e.Graphics, "Charge", label, new Rectangle(Scale(30), 0, Width - Scale(35), Height), text, alignment);
        }
    }

    private static GraphicsPath Outline(Rectangle area, int diameter)
    {
        var path = new GraphicsPath();
        path.AddArc(area.Left, area.Top, diameter, diameter, 90, 180);
        path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 180);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        start = LauncherNative.MousePosition();
        originalLeft = Left;
        dragged = false;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Capture || e.Button != MouseButtons.Left) return;
        int delta = LauncherNative.MousePosition().X - start.X;
        if (!dragged && Math.Abs(delta) < Scale(4)) return;
        dragged = true;
        Cursor = Cursors.SizeWE;
        Left = ClampLeft(originalLeft + delta, Width);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { owner.OpenActions(new Point(Right, Top - 3)); return; }
        if (e.Button != MouseButtons.Left || !Capture) return;
        Capture = false;
        Cursor = Cursors.Hand;
        if (dragged) owner.SaveLauncherOffset(Math.Max(0, rightEdge - Width - Left));
        else owner.OpenDetails(new Point(Right, Top - 3));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }
}

internal static class LauncherNative
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }

    internal static bool Taskbar(out Rectangle area, out int edge)
    {
        area = Rectangle.Empty;
        edge = 0;
        IntPtr shell = FindWindow("Shell_TrayWnd", null);
        Rect taskbar, tray;
        if (shell == IntPtr.Zero || !GetWindowRect(shell, out taskbar)) return false;
        area = Rectangle.FromLTRB(taskbar.Left, taskbar.Top, taskbar.Right, taskbar.Bottom);
        if (area.Height < 24 || area.Height >= area.Width ||
            Rectangle.Intersect(area, Screen.PrimaryScreen.Bounds).Height < 24) return false;
        IntPtr notify = FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null);
        edge = notify != IntPtr.Zero && GetWindowRect(notify, out tray) && tray.Left > area.Left + 100
            ? tray.Left - 8 : area.Right - 260;
        return true;
    }

    internal static Point MousePosition()
    {
        uint pos = GetMessagePos();
        return new Point(unchecked((short)pos), unchecked((short)(pos >> 16)));
    }

    internal static void KeepAboveTaskbar(IntPtr window, Rectangle rect)
    {
        IntPtr hit = WindowFromPoint(new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));
        if (hit == window || hit == IntPtr.Zero) return;
        var name = new StringBuilder(40);
        GetClassName(GetAncestor(hit, 2), name, name.Capacity);
        if (name.ToString() == "Shell_TrayWnd")
            SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x213);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    [DllImport("user32.dll")] private static extern uint GetMessagePos();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
