using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml.Serialization;

public sealed class StripSettings
{
    internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Charge Strip");
    internal static readonly string FileName = Path.Combine(Folder, "settings.xml");

    public string Monitor = "";
    public bool Automatic = true;
    public int X, Y;
    public int Zoom = 100, Gap = 5, Radius = 8, Alpha = 85;
    public string[] HiddenDevices = new string[0], DeviceOrder = new string[0];
    public int MaxDevices = 5, DisplayMode;

    internal List<BatteryReading> Selected(IEnumerable<BatteryReading> readings)
    {
        return Ordered(readings).Where(row => !HiddenDevices.Contains(row.Name)).ToList();
    }

    internal IEnumerable<BatteryReading> Ordered(IEnumerable<BatteryReading> readings)
    {
        return readings.OrderBy(row =>
        {
            int index = Array.IndexOf(DeviceOrder, row.Name);
            return index < 0 ? int.MaxValue : index;
        });
    }

    public string Background = "#1F232B", Text = "#F0F3F7", Icons = "#9BE1C2", Low = "#FFBD78";

    internal StripSettings Copy() { return (StripSettings)MemberwiseClone(); }
    internal static int Clamp(int value, int min, int max) { return Math.Min(max, Math.Max(min, value)); }

    internal static bool TryColor(string hex, out Color color)
    {
        color = Color.Empty;
        int rgb;
        if (hex == null || hex.Length != 7 || hex[0] != '#' ||
            !int.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb)) return false;
        color = Color.FromArgb(rgb | unchecked((int)0xff000000));
        return true;
    }

    internal static Color ColorOf(string hex)
    {
        Color color;
        TryColor(hex, out color);
        return color;
    }

    internal static StripSettings Load()
    {
        try
        {
            using (var file = File.OpenRead(FileName))
            {
                var settings = (StripSettings)new XmlSerializer(typeof(StripSettings)).Deserialize(file);
                settings.Zoom = Clamp(settings.Zoom, 60, 175);
                settings.Gap = Clamp(settings.Gap, 0, 24);
                settings.Radius = Clamp(settings.Radius, 0, 24);
                settings.Alpha = Clamp(settings.Alpha, 0, 100);
                settings.MaxDevices = Clamp(settings.MaxDevices, 1, 12);
                settings.DisplayMode = Clamp(settings.DisplayMode, 0, 2);
                settings.HiddenDevices = settings.HiddenDevices ?? new string[0];
                settings.DeviceOrder = settings.DeviceOrder ?? new string[0];
                var defaults = new StripSettings();
                Color color;
                if (!TryColor(settings.Background, out color)) settings.Background = defaults.Background;
                if (!TryColor(settings.Text, out color)) settings.Text = defaults.Text;
                if (!TryColor(settings.Icons, out color)) settings.Icons = defaults.Icons;
                if (!TryColor(settings.Low, out color)) settings.Low = defaults.Low;
                return settings;
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        string pending = FileName + ".tmp";
        using (var file = File.Create(pending))
            new XmlSerializer(typeof(StripSettings)).Serialize(file, this);
        if (File.Exists(FileName)) File.Replace(pending, FileName, null);
        else File.Move(pending, FileName);
    }
}

internal static class StartupRegistration
{
    internal static string ShortcutPath
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Charge Strip.lnk"); }
    }

    internal static bool Enabled { get { return File.Exists(ShortcutPath); } }

    internal static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(ShortcutPath);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ShortcutPath));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        try
        {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            try
            {
                shortcut.TargetPath = Application.ExecutablePath;
                shortcut.Arguments = "";
                shortcut.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
                shortcut.Save();
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    internal static bool IsAccessError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException ||
               error is System.Security.SecurityException || error is System.Runtime.InteropServices.COMException;
    }
}

internal static class StripArtwork
{
    internal static int CellWidth(StripSettings settings)
    {
        return settings.DisplayMode == 1 ? 25 : settings.DisplayMode == 2 ? 38 : 54;
    }
    internal static Size Measure(int count, StripSettings settings, float scale)
    {
        int shown = Math.Max(1, Math.Min(count, settings.MaxDevices));
        return new Size((int)Math.Ceiling((24 + shown * CellWidth(settings) + (shown - 1) * settings.Gap + (count > settings.MaxDevices ? 26 : 0)) * scale),
            (int)Math.Ceiling(32 * scale));
    }

    internal static void Draw(Graphics graphics, IList<BatteryReading> rows, StripSettings settings, float scale)
    {
        Size size = Measure(rows.Count, settings, scale);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.ScaleTransform(scale, scale);
        var edge = new RectangleF(.75f, .75f, (size.Width - 1.5f) / scale, (size.Height - 1.5f) / scale);
        Color text = StripSettings.ColorOf(settings.Text);
        Color icon = StripSettings.ColorOf(settings.Icons);
        Color low = StripSettings.ColorOf(settings.Low);
        Color background = StripSettings.ColorOf(settings.Background);
        int opacity = Math.Max(1, (settings.Alpha * 255 + 50) / 100);
        using (var path = Outline(edge, settings.Radius))
        using (var fill = new LinearGradientBrush(edge, Color.FromArgb(opacity, ControlPaint.Light(background, .12f)),
            Color.FromArgb(opacity, background), 90f))
        using (var border = new Pen(Color.FromArgb(88, text), 1 / scale))
        using (var grip = new SolidBrush(Color.FromArgb(155, icon)))
        using (var numberFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var iconFont = new Font("Segoe MDL2 Assets", 14, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip })
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            for (int i = 0; i < 3; i++) graphics.FillEllipse(grip, 7, 10 + 5 * i, 2, 2);
            int shown = Math.Max(1, Math.Min(rows.Count, settings.MaxDevices));
            using (var numberBrush = new SolidBrush(text))
            {
                for (int i = 0; i < shown; i++)
                {
                    BatteryReading row = rows.Count > 0 ? rows[i] : null;
                    int x = 16 + i * (CellWidth(settings) + settings.Gap);
                    string glyph = row == null ? "\uE850" : row.Icon;
                    if (settings.DisplayMode != 2)
                        using (var tint = new SolidBrush(row != null && row.Percent <= 25 ? low : icon))
                            graphics.DrawString(glyph, iconFont, tint, new RectangleF(x, 0, 19, 32), format);
                    if (settings.DisplayMode != 1)
                        graphics.DrawString(row == null ? "—" : row.Level, numberFont, numberBrush,
                            new RectangleF(x + (settings.DisplayMode == 2 ? 0 : 20), 0, 39, 32), format);
                }
                if (rows.Count > settings.MaxDevices)
                    graphics.DrawString("+" + (rows.Count - settings.MaxDevices), numberFont, numberBrush,
                        new RectangleF(18 + shown * CellWidth(settings) + (shown - 1) * settings.Gap, 0, 26, 32), format);
            }
        }
    }

    private static GraphicsPath Outline(RectangleF area, int radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(area.Width, area.Height));
        if (diameter == 0) path.AddRectangle(area);
        else
        {
            path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
            path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 90);
            path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(area.Left, area.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
        }
        return path;
    }
}

internal sealed class SettingsWindow : Form
{
    private readonly Strip strip;
    private readonly ComboBox monitor = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 212, AccessibleName = "Монітор" };
    private readonly NumericUpDown x = Number("X", 0, 100000), y = Number("Y", 0, 100000);
    private readonly NumericUpDown zoom = Number("Масштаб", 60, 175), gap = Number("Відступ", 0, 24),
        radius = Number("Кути", 0, 24), alpha = Number("Фон", 0, 100);
    private readonly CheckBox startup = new CheckBox { Text = "Запускати разом із Windows", AutoSize = true };
    private readonly CheckedListBox devices = new CheckedListBox { CheckOnClick = true, Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox displayMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    private readonly NumericUpDown maxDevices = Number("Ліміт пристроїв", 1, 12);
    private readonly Button save = ButtonFor("Зберегти");
    private readonly Label error = new Label { AutoSize = true, ForeColor = Color.Firebrick };
    private readonly TextBox[] hex = new TextBox[4];
    private readonly Button[] palette = new Button[4];
    private readonly Preview preview;
    private bool loading;
    private string topology;

    internal SettingsWindow(Strip owner)
    {
        strip = owner;
        preview = new Preview(owner, SyncPosition) { Dock = DockStyle.Fill };
        Text = "Налаштування";
        Font = new Font("Segoe UI", 9);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 535);
        BackColor = Color.FromArgb(24, 30, 41);
        ForeColor = Color.FromArgb(229, 237, 247);
        StartPosition = FormStartPosition.Manual;
        var work = strip.PositionScreen.WorkingArea;
        Location = new Point(work.Left + Math.Max(0, (work.Width - Width) / 2),
            work.Top + Math.Max(0, (work.Height - Height) / 2));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 338));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var column = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        column.Controls.Add(Heading("Позиція"));
        column.Controls.Add(Line("Монітор", monitor));
        var coordinates = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        coordinates.Controls.Add(x);
        coordinates.Controls.Add(new Label { Text = "Y", AutoSize = true, Margin = new Padding(8, 7, 3, 0) });
        coordinates.Controls.Add(y);
        column.Controls.Add(Line("X", coordinates));
        var resetPosition = ButtonFor("Скинути позицію");
        resetPosition.Click += (sender, args) => { strip.ResetPosition(); Sync(); };
        column.Controls.Add(resetPosition);
        column.Controls.Add(Heading("Розмір і вигляд"));
        column.Controls.Add(Line("Масштаб, %", zoom));
        column.Controls.Add(Line("Відступ, px", gap));
        column.Controls.Add(Line("Кути, px", radius));
        column.Controls.Add(Line("Непрозорість, %", alpha));
        column.Controls.Add(Heading("Кольори"));
        string[] names = { "Фон", "Текст", "Іконки", "Низький заряд" };
        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            hex[i] = new TextBox { Width = 99, MaxLength = 7, AccessibleName = names[i] + " HEX" };
            palette[i] = ButtonFor("Палітра…");
            palette[i].AccessibleName = "Палітра — " + names[i];
            var colorLine = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            colorLine.Controls.Add(hex[i]);
            colorLine.Controls.Add(palette[i]);
            column.Controls.Add(Line(names[i], colorLine));
            hex[i].TextChanged += (sender, args) => AppearanceChanged();
            palette[i].Click += (sender, args) =>
            {
                using (var dialog = new ColorDialog { FullOpen = true, Color = StripSettings.ColorOf(CurrentColors()[index]) })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        hex[index].Text = string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B);
            };
        }

        try { startup.Checked = StartupRegistration.Enabled; }
        catch (Exception problem)
        {
            if (!StartupRegistration.IsAccessError(problem)) throw;
            startup.Enabled = false;
        }
        column.Controls.Add(startup);

        var previewArea = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = new Padding(10, 0, 0, 9) };
        previewArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 195));
        previewArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewArea.Controls.Add(Heading("Прев’ю · перетягніть смужку"));
        previewArea.Controls.Add(preview);
        previewArea.Controls.Add(Heading("Пристрої на смужці"));
        devices.AccessibleName = "Пристрої на смужці";
        previewArea.Controls.Add(devices);
        var deviceActions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
        Button up = ButtonFor("↑"), down = ButtonFor("↓");
        up.AutoSize = down.AutoSize = false;
        up.Size = down.Size = new Size(34, 28);
        up.AccessibleName = "Вище";
        down.AccessibleName = "Нижче";
        up.Click += (sender, args) => MoveDevice(-1);
        down.Click += (sender, args) => MoveDevice(1);
        displayMode.Items.AddRange(new object[] { "Іконка + %", "Тільки іконка", "Тільки %" });
        deviceActions.Controls.AddRange(new Control[] { up, down,
            new Label { Text = "Ліміт", AutoSize = true, Margin = new Padding(8, 8, 3, 0) }, maxDevices, displayMode });
        previewArea.Controls.Add(deviceActions);
        devices.ItemCheck += (sender, args) =>
        {
            if (loading) return;
            string name = (string)devices.Items[args.Index];
            var hidden = strip.Current.HiddenDevices.Where(n => n != name);
            strip.Current.HiddenDevices = (args.NewValue == CheckState.Checked ? hidden : hidden.Concat(new[] { name })).ToArray();
            strip.ApplySettings();
        };
        maxDevices.ValueChanged += (sender, args) => UpdateDeviceLayout();
        displayMode.SelectedIndexChanged += (sender, args) => UpdateDeviceLayout();
        root.Controls.Add(column, 0, 0);
        root.Controls.Add(previewArea, 1, 0);

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var cancel = ButtonFor("Скасувати");
        cancel.DialogResult = DialogResult.Cancel;
        var defaults = ButtonFor("Скинути до стандартних");
        footer.Controls.AddRange(new Control[] { save, cancel, defaults, error });
        root.Controls.Add(footer, 0, 1);
        root.SetColumnSpan(footer, 2);
        Controls.Add(root);
        Style(root);
        save.BackColor = Color.FromArgb(38, 95, 171);
        AcceptButton = save;
        CancelButton = cancel;

        save.Click += (sender, args) => { if (strip.Commit(startup.Checked)) Close(); };
        cancel.Click += (sender, args) => Close();
        defaults.Click += (sender, args) => { strip.ResetSettings(); Sync(); };
        monitor.SelectedIndexChanged += (sender, args) =>
        {
            if (loading || monitor.SelectedIndex < 0) return;
            var selected = (MonitorOption)monitor.SelectedItem;
            strip.SetPosition(selected.Screen.DeviceName, (int)x.Value, (int)y.Value);
            SyncPosition();
        };
        x.ValueChanged += (sender, args) => PositionChanged();
        y.ValueChanged += (sender, args) => PositionChanged();
        x.TextChanged += (sender, args) => PositionChanged();
        y.TextChanged += (sender, args) => PositionChanged();
        x.Leave += (sender, args) => SyncPosition();
        y.Leave += (sender, args) => SyncPosition();
        foreach (var control in new[] { zoom, gap, radius, alpha })
        {
            control.ValueChanged += (sender, args) => AppearanceChanged();
            control.TextChanged += (sender, args) => AppearanceChanged();
        }
        Sync();
    }

    internal void Sync()
    {
        loading = true;
        var settings = strip.Current;
        zoom.Value = settings.Zoom;
        gap.Value = settings.Gap;
        radius.Value = settings.Radius;
        alpha.Value = settings.Alpha;
        maxDevices.Value = settings.MaxDevices;
        displayMode.SelectedIndex = settings.DisplayMode;
        RebuildDevices(true);
        string[] colors = CurrentColors();
        for (int i = 0; i < hex.Length; i++) hex[i].Text = colors[i];
        loading = false;
        ValidateColors();
        SyncPosition();
    }

    internal void SyncPosition()
    {
        if (loading) return;
        loading = true;
        var screens = Screen.AllScreens;
        string signature = string.Join("|", Array.ConvertAll(screens, s => s.DeviceName + s.Bounds));
        if (topology != signature)
        {
            topology = signature;
            monitor.Items.Clear();
            for (int i = 0; i < screens.Length; i++) monitor.Items.Add(new MonitorOption(screens[i], i + 1));
        }
        Screen actual = strip.PositionScreen;
        for (int i = 0; i < monitor.Items.Count; i++)
            if (((MonitorOption)monitor.Items[i]).Screen.DeviceName == actual.DeviceName) monitor.SelectedIndex = i;
        // Keep the user's in-progress coordinate input until the control loses focus.
        if (!x.ContainsFocus) x.Value = StripSettings.Clamp(strip.Left - actual.Bounds.Left, 0, (int)x.Maximum);
        if (!y.ContainsFocus) y.Value = StripSettings.Clamp(strip.Top - actual.Bounds.Top, 0, (int)y.Maximum);
        loading = false;
        RebuildDevices(false);
        preview.Invalidate();
    }

    private void RebuildDevices(bool force)
    {
        var current = strip.Current;
        string[] names = current.Ordered(strip.AllReadings).Select(row => row.Name).Distinct().ToArray();
        if (!force && devices.Items.Cast<string>().SequenceEqual(names)) return;
        string selected = devices.SelectedItem as string;
        bool previous = loading;
        loading = true;
        devices.BeginUpdate();
        devices.Items.Clear();
        foreach (string name in names) devices.Items.Add(name, !current.HiddenDevices.Contains(name));
        if (selected != null && names.Contains(selected)) devices.SelectedItem = selected;
        devices.EndUpdate();
        loading = previous;
    }

    private void MoveDevice(int delta)
    {
        int index = devices.SelectedIndex;
        int target = index + delta;
        if (index < 0 || target < 0 || target >= devices.Items.Count) return;
        var order = devices.Items.Cast<string>().ToList();
        string name = order[index];
        order.RemoveAt(index);
        order.Insert(target, name);
        strip.Current.DeviceOrder = order.Concat(strip.Current.DeviceOrder.Except(order)).ToArray();
        strip.ApplySettings();
    }

    private void UpdateDeviceLayout()
    {
        if (loading || displayMode.SelectedIndex < 0) return;
        strip.Current.MaxDevices = (int)maxDevices.Value;
        strip.Current.DisplayMode = displayMode.SelectedIndex;
        strip.ApplySettings();
    }

    private static void Style(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = Color.FromArgb(229, 237, 247);
            var button = control as Button;
            if (button != null)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(64, 78, 98);
                button.BackColor = Color.FromArgb(39, 51, 68);
            }
            else if (control is TextBox || control is ComboBox || control is NumericUpDown || control is CheckedListBox)
                control.BackColor = Color.FromArgb(35, 45, 61);
            Style(control);
        }
    }

    private void PositionChanged()
    {
        if (loading || monitor.SelectedIndex < 0) return;
        int px, py;
        if (int.TryParse(x.Text, out px) && int.TryParse(y.Text, out py))
        {
            strip.SetPosition(((MonitorOption)monitor.SelectedItem).Screen.DeviceName,
                StripSettings.Clamp(px, 0, (int)x.Maximum), StripSettings.Clamp(py, 0, (int)y.Maximum));
            preview.Invalidate();
        }
    }

    private string[] CurrentColors()
    {
        var s = strip.Current;
        return new[] { s.Background, s.Text, s.Icons, s.Low };
    }

    private void ValidateColors()
    {
        bool valid = true;
        for (int i = 0; i < hex.Length; i++)
        {
            Color color;
            bool correct = StripSettings.TryColor(hex[i].Text, out color);
            valid &= correct;
            hex[i].BackColor = correct ? Color.FromArgb(35, 45, 61) : Color.FromArgb(99, 40, 50);
        }
        error.Text = valid ? "" : "Формат #RRGGBB";
        save.Enabled = valid;
    }

    private void AppearanceChanged()
    {
        if (loading) return;
        var s = strip.Current;
        s.Zoom = ReadNumber(zoom, s.Zoom);
        s.Gap = ReadNumber(gap, s.Gap);
        s.Radius = ReadNumber(radius, s.Radius);
        s.Alpha = ReadNumber(alpha, s.Alpha);
        string[] colors = CurrentColors();
        for (int i = 0; i < hex.Length; i++)
        {
            Color color;
            if (StripSettings.TryColor(hex[i].Text, out color)) colors[i] = hex[i].Text.ToUpperInvariant();
        }
        s.Background = colors[0];
        s.Text = colors[1];
        s.Icons = colors[2];
        s.Low = colors[3];
        ValidateColors();
        strip.ApplySettings();
        SyncPosition();
    }

    private static int ReadNumber(NumericUpDown field, int previous)
    {
        int value;
        return int.TryParse(field.Text, out value) && value >= field.Minimum && value <= field.Maximum ? value : previous;
    }

    private static Button ButtonFor(string text) { return new Button { Text = text, AutoSize = true }; }
    private static NumericUpDown Number(string name, int min, int max)
    {
        return new NumericUpDown { AccessibleName = name, Minimum = min, Maximum = max, Width = 73 };
    }

    private static Label Heading(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(0, 6, 0, 3) };
    }

    private static Control Line(string text, Control control)
    {
        var line = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        line.Controls.Add(new Label { Text = text, Width = 103, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 3, 0) });
        line.Controls.Add(control);
        return line;
    }

    private sealed class MonitorOption
    {
        internal readonly Screen Screen;
        private readonly int number;
        internal MonitorOption(Screen screen, int index) { Screen = screen; number = index; }
        public override string ToString() { return string.Format("{0} · {1} × {2}{3}", number, Screen.Bounds.Width, Screen.Bounds.Height, Screen.Primary ? " · основний" : ""); }
    }

    private sealed class Preview : Control
    {
        private readonly Strip owner;
        private readonly Action positionChanged;
        private RectangleF marker, chip;
        private Point pointer, origin;
        private Rectangle screen;
        private string deviceName;
        private float dragX, dragY, monitorX, monitorY;

        internal Preview(Strip strip, Action onPositionChanged)
        {
            owner = strip;
            positionChanged = onPositionChanged;
            DoubleBuffered = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || (!marker.Contains(e.Location) && !chip.Contains(e.Location))) return;
            Focus();
            screen = owner.PositionScreen.Bounds;
            deviceName = owner.PositionScreen.DeviceName;
            pointer = Native.MouseMessagePosition();
            origin = owner.Location;
            dragX = marker.Contains(e.Location) ? monitorX : 1f;
            dragY = marker.Contains(e.Location) ? monitorY : 1f;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Capture || marker.Contains(e.Location) || chip.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
            if (!Capture || e.Button != MouseButtons.Left) return;
            Point current = Native.MouseMessagePosition();
            owner.SetPosition(deviceName,
                origin.X - screen.Left + (int)Math.Round((current.X - pointer.X) / dragX),
                origin.Y - screen.Top + (int)Math.Round((current.Y - pointer.Y) / dragY));
            positionChanged();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Capture = false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (var fill = new LinearGradientBrush(bounds, Color.FromArgb(65, 86, 110), Color.FromArgb(26, 31, 43), 70f))
                e.Graphics.FillRectangle(fill, bounds);
            var monitor = owner.PositionScreen.Bounds;
            var sample = new RectangleF(21, 22, bounds.Width - 42, Math.Min(bounds.Height * .49f, (bounds.Width - 42) * monitor.Height / monitor.Width));
            using (var band = new SolidBrush(Color.FromArgb(75, 15, 19, 27)))
                e.Graphics.FillRectangle(band, sample);
            using (var border = new Pen(Color.FromArgb(110, 187, 212, 235)))
                e.Graphics.DrawRectangle(border, sample.X, sample.Y, sample.Width, sample.Height);
            monitorX = sample.Width / monitor.Width;
            monitorY = sample.Height / monitor.Height;
            marker = new RectangleF(sample.X + (owner.Left - monitor.Left) * monitorX,
                sample.Y + (owner.Top - monitor.Top) * monitorY, Math.Max(3, owner.Width * monitorX), Math.Max(3, owner.Height * monitorY));
            using (var highlight = new SolidBrush(Color.FromArgb(170, 232, 210)))
                e.Graphics.FillRectangle(highlight, marker);
            using (var band = new SolidBrush(Color.FromArgb(80, 15, 19, 27)))
                e.Graphics.FillRectangle(band, 0, bounds.Height * .74f - 35, bounds.Width, 70);
            float scale = owner.Current.Zoom / 100f * DeviceDpi / 96f;
            var size = StripArtwork.Measure(owner.Readings.Count, owner.Current, scale);
            if (size.Width > bounds.Width - 20)
            {
                scale *= Math.Max(1, bounds.Width - 20) / (float)size.Width;
                size = StripArtwork.Measure(owner.Readings.Count, owner.Current, scale);
            }
            chip = new RectangleF((bounds.Width - size.Width) / 2f, bounds.Height * .74f - size.Height / 2f, size.Width, size.Height);
            e.Graphics.TranslateTransform(chip.X, chip.Y);
            StripArtwork.Draw(e.Graphics, owner.Readings, owner.Current, scale);
        }
    }
}
