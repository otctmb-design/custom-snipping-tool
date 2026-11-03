using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CustomSnipping
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--self-test")
            {
                try { SelfTest.Run(); return 0; }
                catch (Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt"), ex.ToString()); return 1; }
            }
            bool created;
            using (var mutex = new Mutex(true, @"Local\CustomSnippingTool-Shane", out created))
            {
                if (!created)
                {
                    if (args.Length > 0 && (args[0] == "--background" || args[0] == "--startup")) return 0;
                    IntPtr window = Native.FindWindow(null, MainForm.Title);
                    if (window != IntPtr.Zero) { Native.ShowWindow(window, 9); Native.SetForegroundWindow(window); }
                    return 0;
                }
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "Screenshot tool error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                try { Application.Run(new MainForm(args.Length > 0 && (args[0] == "--background" || args[0] == "--startup"))); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Could not start screenshot tool", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            return 0;
        }
    }

    internal static class Images
    {
        public static string Downloads()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"))
            {
                string value = key == null ? null : key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (String.IsNullOrWhiteSpace(value)) throw new IOException("Windows Downloads folder is not configured.");
                string path = Environment.ExpandEnvironmentVariables(value);
                if (!Directory.Exists(path)) throw new DirectoryNotFoundException("Downloads folder does not exist: " + path);
                return path;
            }
        }

        public static string NewPath(string folder, string prefix, string extension)
        {
            return Path.Combine(folder, prefix + "_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension);
        }

        public static string Save(Bitmap image, string directory, string prefix)
        {
            string path = NewPath(directory, prefix, ".png");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                try { image.Save(stream, ImageFormat.Png); }
                catch { stream.Dispose(); File.Delete(path); throw; }
            }
            return path;
        }

        public static Bitmap Crop(Bitmap screen, Rectangle selection, GraphicsPath shape)
        {
            Rectangle area = Rectangle.Intersect(new Rectangle(Point.Empty, screen.Size), selection);
            if (area.Width < 1 || area.Height < 1) throw new ArgumentException("Select a non-empty screen area.");
            var result = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.Clear(Color.Transparent);
                    if (shape != null)
                    {
                        using (var translated = (GraphicsPath)shape.Clone())
                        using (var matrix = new Matrix())
                        {
                            matrix.Translate(-area.X, -area.Y);
                            translated.Transform(matrix);
                            graphics.SetClip(translated);
                        }
                    }
                    graphics.DrawImage(screen, new Rectangle(Point.Empty, area.Size), area, GraphicsUnit.Pixel);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static Bitmap Resize(Bitmap source, int width)
        {
            if (width < 1 || width > 16000) throw new ArgumentOutOfRangeException("width", "Width must be between 1 and 16000 pixels.");
            int height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
            if (height > 16000 || (long)width * height > 64000000) throw new ArgumentException("Resize would be too large. Choose a smaller width.");
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }
    }

    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap screen;
        private readonly Rectangle desktop;
        private readonly string mode;
        private Point start;
        private bool dragging;
        private readonly System.Collections.Generic.List<Point> points = new System.Collections.Generic.List<Point>();
        private Rectangle selection;
        public Bitmap Result { get; private set; }
        public Color PickedColor { get; private set; }

        public CaptureOverlay(string captureMode)
        {
            mode = captureMode;
            desktop = SystemInformation.VirtualScreen;
            screen = new Bitmap(desktop.Width, desktop.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(screen))
                graphics.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = desktop;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImageUnscaled(screen, 0, 0);
            using (var dim = new SolidBrush(Color.FromArgb(65, Color.Black)))
                e.Graphics.FillRectangle(dim, ClientRectangle);
            if (mode == "Freeform" && points.Count > 1)
            {
                using (var pen = new Pen(Color.DeepSkyBlue, 2)) e.Graphics.DrawLines(pen, points.ToArray());
            }
            else if (selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.DrawImage(screen, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.DeepSkyBlue, 2)) e.Graphics.DrawRectangle(pen, selection);
            }
            using (var font = new Font("Segoe UI", 12))
            {
                string instructions = mode == "Window" ? "Click a window" : mode == "Color" ? "Click a color" : "Drag to capture";
                Point cursor = PointToClient(System.Windows.Forms.Cursor.Position);
                var label = new Rectangle(Math.Max(0, Math.Min(Width - 330, cursor.X + 20)), Math.Max(0, Math.Min(Height - 42, cursor.Y + 25)), 330, 40);
                e.Graphics.FillRectangle(Brushes.Black, label);
                e.Graphics.DrawString(instructions + "  |  Esc cancels", font, Brushes.White, label.Location);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (mode == "Color")
            {
                PickedColor = screen.GetPixel(e.X, e.Y);
                DialogResult = DialogResult.OK; Close(); return;
            }
            if (mode == "Window")
            {
                // Hide the overlay before looking up the actual window under it.
                Hide();
                Native.POINT location = new Native.POINT { X = e.X + desktop.X, Y = e.Y + desktop.Y };
                IntPtr window = Native.GetAncestor(Native.WindowFromPoint(location), 2);
                Native.RECT rect;
                if (window == IntPtr.Zero || !Native.GetWindowRect(window, out rect))
                    throw new InvalidOperationException("Could not determine the selected window.");
                selection = Rectangle.FromLTRB(rect.Left - desktop.X, rect.Top - desktop.Y, rect.Right - desktop.X, rect.Bottom - desktop.Y);
                Result = Images.Crop(screen, selection, null);
                DialogResult = DialogResult.OK; Close(); return;
            }
            start = e.Location; dragging = true; Capture = true;
            points.Clear(); points.Add(start);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
                if (mode == "Freeform") points.Add(e.Location);
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false; Capture = false;
            selection = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X), Math.Max(start.Y, e.Y));
            if (mode == "Freeform")
            {
                points.Add(e.Location);
                if (points.Count < 3) return;
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(points.ToArray());
                    selection = Rectangle.Ceiling(path.GetBounds());
                    if (selection.Width < 2 || selection.Height < 2) return;
                    Result = Images.Crop(screen, selection, path);
                }
            }
            else
            {
                if (selection.Width < 2 || selection.Height < 2) return;
                Result = Images.Crop(screen, selection, null);
            }
            DialogResult = DialogResult.OK; Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) screen.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class Preferences
    {
        private const string KeyPath = @"Software\CustomSnippingTool";
        internal bool AutoSave = true, ShowAfterCapture = true, StartMinimized = true, Animate = true, Shortcuts = true;

        internal static Preferences Load()
        {
            var result = new Preferences();
            using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
            {
                if (key == null) return result;
                result.AutoSave = Read(key, "AutoSave", true);
                result.ShowAfterCapture = Read(key, "ShowAfterCapture", true);
                result.StartMinimized = Read(key, "StartMinimized", true);
                result.Animate = Read(key, "Animate", true);
                result.Shortcuts = Read(key, "Shortcuts", true);
            }
            return result;
        }

        private static bool Read(RegistryKey key, string name, bool defaultValue)
        {
            object value = key.GetValue(name);
            if (value == null) return defaultValue;
            if (!(value is int) || ((int)value != 0 && (int)value != 1))
                throw new InvalidDataException("Invalid setting: " + name + ". Expected 0 or 1 in " + KeyPath + ".");
            return (int)value == 1;
        }

        internal void Save()
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                key.SetValue("AutoSave", AutoSave ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("ShowAfterCapture", ShowAfterCapture ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("StartMinimized", StartMinimized ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Animate", Animate ? 1 : 0, RegistryValueKind.DWord);
                key.SetValue("Shortcuts", Shortcuts ? 1 : 0, RegistryValueKind.DWord);
            }
        }
    }

    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "CustomSnippingTool";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" --startup"; } }
        private static string LegacyPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Custom Snipping Tool.lnk"); } }

        private static bool HasManagedShortcut()
        {
            if (!File.Exists(LegacyPath)) return false;
            Type type = Type.GetTypeFromProgID("WScript.Shell", true);
            object shell = Activator.CreateInstance(type), shortcut = null;
            try
            {
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { LegacyPath });
                object target = shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                return String.Equals(target as string, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (shortcut != null) Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        internal static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                object existing = key == null ? null : key.GetValue(Name);
                if (existing != null && !String.Equals(existing as string, Command, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A different startup command already uses " + Name + ". It will not be overwritten.");
                return existing != null || HasManagedShortcut();
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            IsEnabled();
            bool legacy = HasManagedShortcut();
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) key.SetValue(Name, Command);
                else key.DeleteValue(Name, false);
            }
            if (legacy) File.Delete(LegacyPath);
        }
    }

    internal sealed class StudioButton : Button
    {
        internal static bool AnimationsEnabled = true;
        private readonly System.Windows.Forms.Timer animation = new System.Windows.Forms.Timer { Interval = 16 };
        private bool hovered, pressed;
        private float glow, target;
        internal StudioButton()
        {
            DoubleBuffered = true;
            animation.Tick += delegate
            {
                glow += (target - glow) * 0.3f;
                if (Math.Abs(target - glow) < 0.015f) { glow = target; animation.Stop(); }
                Invalidate();
            };
        }
        private void Transition()
        {
            target = !Enabled ? 0 : pressed ? 1 : hovered ? 0.65f : 0;
            if (AnimationsEnabled && SystemInformation.IsMenuAnimationEnabled) animation.Start();
            else { animation.Stop(); glow = target; Invalidate(); }
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Transition(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); Transition(); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Transition(); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Transition(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Transition(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = Enabled ? Blend(BackColor, FlatAppearance.MouseOverBackColor, glow) : Color.FromArgb(29, 36, 48);
            var area = new Rectangle(0, 0, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            using (var shadow = new SolidBrush(Color.FromArgb(12, 16, 23))) e.Graphics.FillRectangle(shadow, 3, 3, area.Width, area.Height);
            using (var brush = new LinearGradientBrush(area, Blend(fill, Color.White, 0.04f), fill, 90f)) e.Graphics.FillRectangle(brush, area);
            using (var pen = new Pen(Enabled ? Blend(FlatAppearance.BorderColor, Color.FromArgb(113, 192, 255), glow) : Color.FromArgb(48, 61, 80)))
                e.Graphics.DrawRectangle(pen, area);
            TextRenderer.DrawText(e.Graphics, Text, Font, area,
                Enabled ? ForeColor : Color.FromArgb(132, 149, 173),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(area, -4, -4), ForeColor, fill);
        }
        private static Color Blend(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class BorderedPanel : Panel
    {
        internal BorderedPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(27, 33, 44);
            Padding = new Padding(12);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(63, 82, 110)))
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    internal sealed class MainForm : Form
    {
        public const string Title = "Custom Snipping Tool";
        private readonly PictureBox preview = new PictureBox();
        private readonly TextBox text = new TextBox();
        private readonly Label status = new Label();
        private readonly Label bindings = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Label emptyPreview = new Label();
        private readonly string clipboardMarker = "CustomSnippingTool." + Guid.NewGuid().ToString("N");
        private uint clipboardSequence;
        private readonly System.Collections.Generic.List<Button> imageButtons = new System.Collections.Generic.List<Button>();
        private Button clearButton;
        private Button saveButton;
        private readonly System.Collections.Generic.List<ToolStripMenuItem> imageMenuItems = new System.Collections.Generic.List<ToolStripMenuItem>();
        private readonly Preferences preferences;
        private readonly ContextMenuStrip toolsMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip captureMenu = new ContextMenuStrip();
        private readonly bool background;
        private Bitmap current;
        private string currentPath;
        private bool busy, exiting, keysEnabled = true;
        private IntPtr hook;
        private Native.HookProc hookProc;
        private bool shortcutRegistered;

        public MainForm(bool startInBackground)
        {
            preferences = Preferences.Load();
            keysEnabled = preferences.Shortcuts; StudioButton.AnimationsEnabled = preferences.Animate;
            background = startInBackground && preferences.StartMinimized;
            Text = Title; Size = new Size(1040, 780); MinimumSize = new Size(820, 620);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.None; Padding = new Padding(1); DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(18, 23, 32); ForeColor = Color.FromArgb(229, 237, 249);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18, 8, 18, 16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            var header = new Panel { Dock = DockStyle.Fill };
            var heading = new Label { Text = "SCREENSHOT STUDIO", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 17, FontStyle.Bold), ForeColor = Color.FromArgb(100, 177, 255) };
            heading.MouseDown += DragWindow; header.MouseDown += DragWindow;
            heading.DoubleClick += delegate { ToggleMaximize(); };
            var windowControls = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 156, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            AddWindowButton(windowControls, "_", "Minimize", delegate { WindowState = FormWindowState.Minimized; });
            AddWindowButton(windowControls, "+", "Maximize or restore", ToggleMaximize);
            AddWindowButton(windowControls, "X", "Close to tray", Hide);
            var brand = new PictureBox { Dock = DockStyle.Left, Width = 44, SizeMode = PictureBoxSizeMode.CenterImage, Image = Icon.ToBitmap(), AccessibleName = "Screenshot Studio icon" };
            brand.MouseDown += DragWindow;
            brand.DoubleClick += delegate { ToggleMaximize(); };
            header.Controls.Add(heading); header.Controls.Add(windowControls); header.Controls.Add(brand);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            foreach (string mode in new[] { "Rectangle", "Freeform", "Window", "Full screen" })
            {
                string chosen = mode;
                captureMenu.Items.Add(chosen, null, delegate { Guard(delegate { CaptureScreen(chosen); }); });
            }
            Button captureButton = null, toolsButton = null;
            captureButton = AddButton(toolbar, "Capture...", delegate { captureMenu.Show(captureButton, new Point(0, captureButton.Height)); }, true);
            saveButton = AddButton(toolbar, "Save", SaveCurrent); imageButtons.Add(saveButton);
            clearButton = AddButton(toolbar, "Clear", ClearContent);
            clearButton.AccessibleDescription = "Clear preview and text, and only this app's clipboard copy. Saved files are preserved.";
            toolsButton = AddButton(toolbar, "Tools...", delegate { toolsMenu.Show(toolsButton, new Point(0, toolsButton.Height)); });
            AddButton(toolbar, "Settings", OpenSettings);
            imageMenuItems.Add(AddTool("Copy image", CopyCurrent));
            imageMenuItems.Add(AddTool("Extract text (OCR)", async delegate { await ExtractText(); }));
            imageMenuItems.Add(AddTool("Resize a copy", ResizeCurrent));
            toolsMenu.Items.Add(new ToolStripSeparator());
            AddTool("Pick screen color", delegate { CaptureScreen("Color"); });
            AddTool("Save clipboard text", SaveClipboardText);
            AddTool("Open Downloads", delegate { Process.Start("explorer.exe", "\"" + Images.Downloads() + "\""); });
            StyleMenu(captureMenu); StyleMenu(toolsMenu);
            bindings.Dock = DockStyle.Fill; bindings.ForeColor = Color.FromArgb(151, 174, 203); bindings.AutoEllipsis = true;
            preview.Dock = DockStyle.Fill; preview.SizeMode = PictureBoxSizeMode.Zoom; preview.BackColor = Color.FromArgb(20, 25, 34); preview.AccessibleName = "Screenshot preview";
            emptyPreview.Text = "Your next capture appears here\r\n\r\nChoose a capture mode or press Shift + \\";
            emptyPreview.Dock = DockStyle.Fill; emptyPreview.TextAlign = ContentAlignment.MiddleCenter;
            emptyPreview.BackColor = preview.BackColor; emptyPreview.ForeColor = Color.FromArgb(151, 174, 203);
            var previewArea = new Panel { Dock = DockStyle.Fill };
            previewArea.Controls.Add(preview); previewArea.Controls.Add(emptyPreview); emptyPreview.BringToFront();
            text.Dock = DockStyle.Fill; text.Multiline = true; text.ScrollBars = ScrollBars.Both; text.ReadOnly = true; text.BorderStyle = BorderStyle.None;
            text.BackColor = Color.FromArgb(27, 33, 44); text.ForeColor = ForeColor; text.AccessibleName = "Extracted text and color values";
            var previewCard = CreateCard("PREVIEW", previewArea);
            var textCard = CreateCard("TEXT & COLOR", text);
            status.Dock = DockStyle.Fill; status.Text = CaptureHint();
            status.ForeColor = Color.FromArgb(151, 174, 203); status.AccessibleName = "Action status";
            var statusCard = new BorderedPanel { Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 0) };
            statusCard.Controls.Add(status);
            layout.Controls.Add(header, 0, 0); layout.Controls.Add(toolbar, 0, 1); layout.Controls.Add(bindings, 0, 2);
            layout.Controls.Add(previewCard, 0, 3); layout.Controls.Add(textCard, 0, 4); layout.Controls.Add(statusCard, 0, 5);
            Controls.Add(layout);
            UpdateImageActions();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open screenshot tool", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            menu.Items.Add("Capture rectangle", null, delegate { CaptureScreen("Rectangle"); });
            menu.Items.Add("Settings", null, delegate { Guard(OpenSettings); });
            menu.Items.Add("Open Downloads", null, delegate { Guard(delegate { Process.Start("explorer.exe", "\"" + Images.Downloads() + "\""); }); });
            menu.Items.Add("Exit and restore normal keys", null, delegate { exiting = true; Close(); });
            StyleMenu(menu);
            tray.Icon = Icon; tray.Text = Title; tray.ContextMenuStrip = menu; tray.Visible = true;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            Shown += delegate { ConfigureKeys(); if (background) Hide(); };
        }

        private BorderedPanel CreateCard(string title, Control content)
        {
            var card = new BorderedPanel { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 8) };
            var rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rows.Controls.Add(new Label { Text = title, UseMnemonic = false, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = Color.FromArgb(100, 177, 255) }, 0, 0);
            rows.Controls.Add(content, 0, 1); card.Controls.Add(rows);
            return card;
        }

        private string CaptureHint()
        {
            return (preferences.AutoSave ? "Auto-save ON: captures save to Downloads and copy to clipboard." :
                "Auto-save OFF: captures copy to clipboard. Use Save to keep a PNG.") +
                (preferences.ShowAfterCapture ? " Preview opens after capture." : " Capture quietly; reopen from the tray.");
        }

        private ToolStripMenuItem AddTool(string title, Action action)
        {
            var item = new ToolStripMenuItem(title);
            item.Click += delegate { Guard(action); };
            toolsMenu.Items.Add(item);
            return item;
        }

        private void StyleMenu(ContextMenuStrip menu)
        {
            menu.BackColor = Color.FromArgb(27, 33, 44); menu.ForeColor = ForeColor; menu.Font = Font;
            menu.ShowImageMargin = false; menu.RenderMode = ToolStripRenderMode.System;
        }

        private void AddWindowButton(FlowLayoutPanel panel, string glyph, string accessibleName, Action action)
        {
            var button = AddButton(panel, glyph, action);
            button.AccessibleName = accessibleName;
            button.MinimumSize = new Size(44, 34); button.AutoSize = false; button.Size = new Size(44, 34);
            button.Padding = Padding.Empty; button.Margin = new Padding(0, 0, 6, 0);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ClassStyle |= 0x00020000; return parameters; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(73, 115, 167)))
                e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }

        private Form CreateDialog(string title, Size size)
        {
            var dialog = new Form { Text = title, Size = size, BackColor = BackColor, ForeColor = ForeColor, Font = Font,
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Padding = new Padding(18) };
            dialog.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var pen = new Pen(Color.FromArgb(73, 115, 167))) e.Graphics.DrawRectangle(pen, 0, 0, dialog.Width - 1, dialog.Height - 1);
            };
            var heading = new Label { Text = title, Location = new Point(18, 15), Size = new Size(size.Width - 92, 32), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            heading.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(dialog.Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            };
            var close = new StudioButton { Text = "X", AccessibleName = "Close dialog", DialogResult = DialogResult.Cancel, Location = new Point(size.Width - 56, 15), Size = new Size(36, 32),
                BackColor = Color.FromArgb(34, 44, 60), ForeColor = ForeColor, FlatStyle = FlatStyle.Flat };
            dialog.Controls.Add(heading); dialog.Controls.Add(close); dialog.CancelButton = close;
            return dialog;
        }

        private void OpenSettings()
        {
            if (busy) throw new InvalidOperationException("Wait for the current action to finish before changing settings.");
            bool startupEnabled = Startup.IsEnabled();
            Show(); WindowState = FormWindowState.Normal;
            using (var dialog = CreateDialog("Settings", new Size(580, 445)))
            {
                var automatic = new CheckBox { Text = "Automatically save captures to Downloads", Checked = preferences.AutoSave };
                var show = new CheckBox { Text = "Open the preview after a screenshot", Checked = preferences.ShowAfterCapture };
                var startup = new CheckBox { Text = "Start with Windows (at sign-in)", Checked = startupEnabled };
                var minimize = new CheckBox { Text = "Start minimized to the tray when Windows starts", Checked = preferences.StartMinimized };
                var shortcuts = new CheckBox { Text = "Enable Shift + \\ and Print Screen capture shortcuts", Checked = preferences.Shortcuts };
                var animations = new CheckBox { Text = "Animate button hover and click fades", Checked = preferences.Animate };
                int y = 65;
                foreach (var check in new[] { automatic, show, startup, minimize, shortcuts, animations })
                {
                    check.Location = new Point(22, y); check.AutoSize = true; y += 38; dialog.Controls.Add(check);
                }
                minimize.Enabled = startup.Checked;
                startup.CheckedChanged += delegate { minimize.Enabled = startup.Checked; };
                var hint = new Label { Text = "Auto-save OFF keeps captures in memory and on the clipboard until\r\nsaved, replaced, or cleared. Shift + \\ replaces typing | while enabled.",
                    Location = new Point(22, 302), Size = new Size(530, 55), ForeColor = Color.FromArgb(151, 174, 203) };
                var buttons = new FlowLayoutPanel { Location = new Point(22, 367), Size = new Size(530, 55), FlowDirection = FlowDirection.RightToLeft };
                AddButton(buttons, "Apply", delegate
                {
                    Guard(delegate
                    {
                        var pending = new Preferences { AutoSave = automatic.Checked, ShowAfterCapture = show.Checked,
                            StartMinimized = minimize.Checked, Shortcuts = shortcuts.Checked, Animate = animations.Checked };
                        if (startup.Checked != startupEnabled) Startup.SetEnabled(startup.Checked);
                        pending.Save();
                        preferences.AutoSave = pending.AutoSave; preferences.ShowAfterCapture = pending.ShowAfterCapture;
                        preferences.StartMinimized = pending.StartMinimized; preferences.Shortcuts = pending.Shortcuts; preferences.Animate = pending.Animate;
                        StudioButton.AnimationsEnabled = preferences.Animate;
                        bool changedKeys = keysEnabled != preferences.Shortcuts;
                        keysEnabled = preferences.Shortcuts;
                        if (changedKeys) ConfigureKeys();
                        status.Text = "Settings saved. " + CaptureHint(); dialog.DialogResult = DialogResult.OK; dialog.Close();
                    });
                }, true);
                var cancel = AddButton(buttons, "Cancel", dialog.Close); dialog.CancelButton = cancel;
                dialog.Controls.Add(hint); dialog.Controls.Add(buttons); dialog.ShowDialog(this);
            }
        }

        private Button AddButton(FlowLayoutPanel panel, string label, Action action, bool primary = false)
        {
            var button = new StudioButton { Text = label, AutoSize = true, MinimumSize = new Size(100, 38), Padding = new Padding(9, 4, 9, 4), Margin = new Padding(0, 0, 8, 8), FlatStyle = FlatStyle.Flat };
            button.BackColor = primary ? Color.FromArgb(35, 105, 190) : Color.FromArgb(34, 44, 60);
            button.ForeColor = Color.FromArgb(239, 245, 255);
            button.FlatAppearance.BorderColor = Color.FromArgb(70, 97, 133);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(46, 91, 145);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(27, 74, 128);
            button.UseVisualStyleBackColor = false;
            button.Click += delegate { Guard(action); };
            panel.Controls.Add(button);
            return button;
        }

        private void UpdateImageActions()
        {
            foreach (Button button in imageButtons) button.Enabled = current != null && !busy;
            foreach (ToolStripMenuItem item in imageMenuItems) item.Enabled = current != null && !busy;
            clearButton.Enabled = !busy;
            saveButton.Text = currentPath == null ? "Save" : "Save copy";
        }

        private void CopyOwnedData(DataObject data)
        {
            data.SetData(clipboardMarker, false, "owned");
            ClipboardAction(delegate
            {
                Clipboard.SetDataObject(data, true);
                clipboardSequence = Native.GetClipboardSequenceNumber();
            });
        }

        private void CopyOwnedText(string value)
        {
            var data = new DataObject(); data.SetText(value); CopyOwnedData(data);
        }

        private void CopyOwnedImage()
        {
            var data = new DataObject(); data.SetImage(current); CopyOwnedData(data);
        }

        private void ClearContent()
        {
            if (busy) throw new InvalidOperationException("Wait for the current action to finish before clearing.");
            preview.Image = null;
            if (current != null) { current.Dispose(); current = null; }
            currentPath = null; text.Clear(); emptyPreview.Visible = true;
            UpdateImageActions();
            bool cleared = false;
            if (clipboardSequence != 0)
            {
                // Lock the clipboard during ownership checking so a newer external copy cannot be erased.
                ClipboardAction(delegate
                {
                    if (!Native.OpenClipboard(Handle)) throw new ExternalException("Clipboard is busy. Preview and text were cleared; clipboard was not cleared.");
                    try
                    {
                        uint format = Native.RegisterClipboardFormat(clipboardMarker);
                        if (format == 0) throw new ExternalException("Could not check clipboard ownership.");
                        if (Native.GetClipboardSequenceNumber() == clipboardSequence && Native.IsClipboardFormatAvailable(format))
                        {
                            if (!Native.EmptyClipboard()) throw new ExternalException("Could not clear the app's clipboard copy.");
                            cleared = true;
                        }
                    }
                    finally { Native.CloseClipboard(); }
                });
            }
            clipboardSequence = 0;
            status.Text = cleared ? "Cleared preview, text, and this app's clipboard copy. Saved files were kept." :
                "Cleared preview and text. Clipboard contents and saved files were kept.";
        }

        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception ex) { Error(ex); }
        }

        private void Error(Exception ex)
        {
            status.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Could not complete action", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ConfigureKeys()
        {
            if (shortcutRegistered) { Native.UnregisterHotKey(Handle, 1); shortcutRegistered = false; }
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            if (!keysEnabled) { bindings.Text = "Shortcuts paused. Shift + \\ and Print Screen work normally."; return; }
            shortcutRegistered = Native.RegisterHotKey(Handle, 1, 0x0004 | 0x4000, 0xDC);
            hookProc = KeyboardHook;
            hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0);
            bindings.Text = "Shift + \\: " + (shortcutRegistered ? "capture" : "UNAVAILABLE (already assigned)") +
                "    Print Screen: " + (hook != IntPtr.Zero ? "capture" : "UNAVAILABLE") + "    Fn + Y: hardware-dependent";
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkey-status.txt"), bindings.Text);
            if (!shortcutRegistered || hook == IntPtr.Zero)
                Error(new InvalidOperationException("Some capture shortcuts could not be installed. " + bindings.Text));
        }

        private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                Native.KBDLLHOOKSTRUCT key = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(Native.KBDLLHOOKSTRUCT));
                if (key.VkCode == 0x2C && (key.Flags & 0x10) == 0 && Control.ModifierKeys == Keys.None)
                {
                    if (message.ToInt32() == 0x101 && !busy)
                        BeginInvoke((Action)delegate { Guard(delegate { CaptureScreen("Rectangle"); }); });
                    return new IntPtr(1);
                }
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == 1 && !busy)
                BeginInvoke((Action)delegate { Guard(delegate { CaptureScreen("Rectangle"); }); });
            if (message.Msg == 0x0024)
            {
                var bounds = (Native.MINMAXINFO)Marshal.PtrToStructure(message.LParam, typeof(Native.MINMAXINFO));
                var screen = Screen.FromHandle(Handle);
                bounds.MaxPosition = new Native.POINT { X = screen.WorkingArea.X - screen.Bounds.X, Y = screen.WorkingArea.Y - screen.Bounds.Y };
                bounds.MaxSize = new Native.POINT { X = screen.WorkingArea.Width, Y = screen.WorkingArea.Height };
                bounds.MinTrackSize = new Native.POINT { X = MinimumSize.Width, Y = MinimumSize.Height };
                Marshal.StructureToPtr(bounds, message.LParam, false); message.Result = IntPtr.Zero; return;
            }
            if (message.Msg == 0x0084 && WindowState != FormWindowState.Maximized)
            {
                long position = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
                bool left = point.X < 7, right = point.X >= ClientSize.Width - 7;
                bool top = point.Y < 7, bottom = point.Y >= ClientSize.Height - 7;
                int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
                if (hit != 0) { message.Result = new IntPtr(hit); return; }
            }
            base.WndProc(ref message);
        }

        private void ClipboardAction(Action action)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { action(); return; }
                catch (ExternalException) { if (attempt >= 4) throw; Thread.Sleep(100); }
            }
        }

        private void SetCurrent(Bitmap image, string path)
        {
            Bitmap old = current; current = image; currentPath = path; preview.Image = current;
            if (old != null) old.Dispose();
            emptyPreview.Visible = false; UpdateImageActions();
            text.Clear(); status.Text = path == null ? "Captured " + image.Width + " x " + image.Height + ". Not saved yet." :
                "Saved " + image.Width + " x " + image.Height + " PNG: " + path;
        }

        private void CaptureScreen(string mode)
        {
            if (busy) return;
            bool wasVisible = Visible, completed = false, failed = true;
            FormWindowState previousState = WindowState;
            busy = true;
            try
            {
                Hide();
                // Let the desktop compositor remove this window before freezing the screen.
                Application.DoEvents(); Thread.Sleep(200);
                if (mode == "Full screen")
                {
                    Rectangle desktop = SystemInformation.VirtualScreen;
                    using (var image = new Bitmap(desktop.Width, desktop.Height))
                    {
                        using (Graphics graphics = Graphics.FromImage(image)) graphics.CopyFromScreen(desktop.Location, Point.Empty, desktop.Size);
                        SaveCapture(new Bitmap(image));
                        completed = true;
                    }
                }
                else
                {
                    using (var overlay = new CaptureOverlay(mode))
                    {
                        if (overlay.ShowDialog() == DialogResult.OK)
                        {
                            if (mode == "Color")
                            {
                                Color color = overlay.PickedColor;
                                string hex = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
                                CopyOwnedText(hex);
                                text.Text = hex + Environment.NewLine + "RGB(" + color.R + ", " + color.G + ", " + color.B + ")";
                                status.Text = "Color copied to clipboard: " + hex;
                            }
                            else SaveCapture(overlay.Result);
                            completed = true;
                        }
                    }
                }
                failed = false;
            }
            finally
            {
                busy = false; UpdateImageActions();
                bool show = failed || (completed && (preferences.ShowAfterCapture || mode == "Color")) || (!completed && wasVisible);
                if (show) { Show(); WindowState = previousState == FormWindowState.Minimized ? FormWindowState.Normal : previousState; Activate(); }
                else
                {
                    Hide();
                    if (completed) tray.ShowBalloonTip(1800, "Screenshot captured", status.Text, ToolTipIcon.Info);
                }
            }
        }

        private void SaveCapture(Bitmap image, bool forceSave = false)
        {
            SetCurrent(image, null);
            if (preferences.AutoSave || forceSave) SaveCurrent();
            try { CopyOwnedImage(); }
            catch (ExternalException ex) { throw new InvalidOperationException((currentPath == null ? "The capture is in the preview but is not saved." :
                "The PNG was saved at " + currentPath + ".") + " Clipboard copying failed. Use Tools > Copy image to retry.", ex); }
            status.Text = currentPath == null ? "Copied screenshot to clipboard. Auto-save is OFF; use Save to keep a PNG." :
                "Saved PNG and copied to clipboard: " + currentPath;
        }

        private void SaveCurrent()
        {
            RequireImage();
            currentPath = Images.Save(current, Images.Downloads(), "Snip");
            UpdateImageActions(); status.Text = "Saved PNG: " + currentPath;
        }

        private void RequireImage()
        {
            if (current == null) throw new InvalidOperationException("Capture a screenshot first.");
        }

        private void CopyCurrent()
        {
            RequireImage(); CopyOwnedImage();
            status.Text = currentPath == null ? "Image copied to clipboard. Use Save to keep a PNG." : "Image copied to clipboard. Saved file: " + currentPath;
        }

        private void ResizeCurrent()
        {
            RequireImage();
            using (var dialog = CreateDialog("Resize a copy (original is preserved)", new Size(490, 190)))
            {
                var label = new Label { Text = "Width in pixels (aspect ratio is preserved):", AutoSize = true, Location = new Point(22, 67) };
                var width = new NumericUpDown { Minimum = 1, Maximum = 16000, Value = Math.Min(current.Width, 16000), Location = new Point(22, 105), Width = 160, BackColor = Color.FromArgb(34, 44, 60), ForeColor = ForeColor };
                var save = new StudioButton { Text = "Save resized copy", DialogResult = DialogResult.OK, AutoSize = true, Location = new Point(205, 101), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 105, 190), ForeColor = ForeColor, Padding = new Padding(6) };
                dialog.Controls.AddRange(new Control[] { label, width, save }); dialog.AcceptButton = save;
                if (dialog.ShowDialog(this) == DialogResult.OK) SaveCapture(Images.Resize(current, (int)width.Value), true);
            }
        }

        private async Task ExtractText()
        {
            if (busy) return;
            string temporary = null;
            try
            {
                RequireImage(); busy = true; UpdateImageActions(); status.Text = "Extracting text locally with Windows OCR...";
                string imagePath = currentPath;
                if (imagePath == null) { temporary = Images.Save(current, Path.GetTempPath(), "CustomSnipOCR"); imagePath = temporary; }
                string output = await Task.Run(delegate
                {
                    string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Read-ScreenshotText.ps1");
                    var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -ImagePath \"" + imagePath + "\"");
                    info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
                    info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
                    using (Process process = Process.Start(info))
                    {
                        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                        Task<string> stderr = process.StandardError.ReadToEndAsync();
                        if (!process.WaitForExit(60000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Windows OCR timed out. Try a smaller screenshot."); }
                        Task.WaitAll(stdout, stderr);
                        if (process.ExitCode != 0) throw new InvalidOperationException("Windows OCR failed: " + stderr.Result.Trim());
                        return stdout.Result;
                    }
                });
                text.Text = output;
                if (String.IsNullOrWhiteSpace(output)) { status.Text = "No readable text found. Try a closer or clearer capture."; return; }
                CopyOwnedText(output);
                status.Text = "Extracted text copied to clipboard. Use Save clipboard text to save it.";
            }
            catch (Exception ex) { Error(ex); }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException ex) { Error(ex); }
                    catch (UnauthorizedAccessException ex) { Error(ex); }
                }
                busy = false; UpdateImageActions();
            }
        }

        private void SaveClipboardText()
        {
            string value = null;
            ClipboardAction(delegate { if (Clipboard.ContainsText()) value = Clipboard.GetText(); });
            if (String.IsNullOrEmpty(value)) throw new InvalidOperationException("There is no text on the clipboard. Copy text or use Extract text first.");
            string path = Images.NewPath(Images.Downloads(), "Clipboard", ".txt");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(value);
            text.Text = value; status.Text = "Clipboard text saved: " + path;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
            if (shortcutRegistered) Native.UnregisterHotKey(Handle, 1);
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            tray.Visible = false; tray.Dispose(); preview.Image = null;
            captureMenu.Dispose(); toolsMenu.Dispose();
            if (current != null) current.Dispose();
            base.OnFormClosing(e);
        }
    }

    internal static class Native
    {
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct MINMAXINFO { public POINT Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public uint VkCode, ScanCode, Flags, Time; public IntPtr ExtraInfo; }
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flag);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string className, string title);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterClipboardFormat(string format);
        [DllImport("user32.dll")] internal static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] internal static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] internal static extern bool EmptyClipboard();
        [DllImport("user32.dll")] internal static extern bool CloseClipboard();
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }

    internal static class SelfTest
    {
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        internal static void Run()
        {
            string folder = Path.Combine(Path.GetTempPath(), "CustomSnipTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var files = new System.Collections.Generic.List<string>();
            try
            {
                using (var image = new Bitmap(100, 60))
                {
                    using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
                    using (var crop = Images.Crop(image, new Rectangle(10, 10, 20, 15), null))
                        Check(crop.Size == new Size(20, 15) && crop.GetPixel(5, 5).ToArgb() == Color.Red.ToArgb(), "Rectangle crop failed.");
                    using (var crop = Images.Crop(image, new Rectangle(-10, -10, 30, 30), null))
                        Check(crop.Size == new Size(20, 20), "Off-screen window bounds failed.");
                    using (var path = new GraphicsPath())
                    {
                        path.AddPolygon(new[] { new Point(0, 0), new Point(40, 0), new Point(0, 40) });
                        using (var crop = Images.Crop(image, new Rectangle(0, 0, 40, 40), path))
                            Check(crop.GetPixel(3, 3).R == 255 && crop.GetPixel(35, 35).A == 0, "Freeform transparent crop failed.");
                    }
                    using (var resized = Images.Resize(image, 50))
                        Check(resized.Size == new Size(50, 30), "Aspect-ratio resize failed.");
                    bool invalid = false;
                    try { Images.Resize(image, 0); } catch (ArgumentOutOfRangeException) { invalid = true; }
                    Check(invalid, "Invalid resize was accepted.");
                    files.Add(Images.Save(image, folder, "Snip")); files.Add(Images.Save(image, folder, "Snip"));
                    Check(files[0] != files[1], "Save filename collision.");
                    using (var loaded = new Bitmap(files[0]))
                        Check(loaded.Size == image.Size && loaded.RawFormat.Guid == ImageFormat.Png.Guid, "PNG save failed.");
                    Check(Directory.Exists(Images.Downloads()), "Downloads detection failed.");
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test-result.txt"),
                    "PASS: Rectangle crop, off-screen window clipping, freeform transparency, resize aspect ratio, invalid resize rejection, unique PNG saves, PNG roundtrip, and Downloads detection.");
            }
            finally { foreach (string file in files) File.Delete(file); Directory.Delete(folder); }
        }
    }
}
