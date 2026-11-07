// Shared look for Nevatch and Nevatch Server: a window with a sidebar, the shield logo, a dotted
// world map, a big status view, buttons, cards and switches, all painted with GDI+ (.NET Framework 4).
// C# 5 only: both apps are built with the compiler that ships with Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Nevatch.Ui
{
    public static class Theme
    {
        public static readonly float Scale = ScreenScale();

        // A deep navy palette: indigo accent, near-black navy edges, slightly lighter navy surfaces.
        public static readonly Color Accent = Color.FromArgb(94, 99, 240);
        public static readonly Color AccentLight = Color.FromArgb(118, 122, 255);
        public static readonly Color AccentDark = Color.FromArgb(76, 80, 214);
        public static readonly Color AccentSoft = Color.FromArgb(124, 118, 214); // the calmer button on cards
        public static readonly Color Text = Color.FromArgb(240, 241, 248);
        public static readonly Color NavText = Color.FromArgb(146, 151, 176);
        public static readonly Color Muted = Color.FromArgb(178, 183, 204);
        public static readonly Color Faint = Color.FromArgb(62, 66, 90);
        public static readonly Color Rail = Color.FromArgb(17, 19, 30);
        public static readonly Color SidebarBg = Color.FromArgb(21, 23, 36);
        public static readonly Color PresenceBg = Color.FromArgb(17, 19, 30);
        public static readonly Color ContentBg = Color.FromArgb(26, 28, 43);
        public static readonly Color CardBg = Color.FromArgb(33, 36, 54);
        public static readonly Color CardHover = Color.FromArgb(39, 42, 62);
        public static readonly Color CardBorder = Color.FromArgb(44, 47, 70);
        public static readonly Color InputBg = Color.FromArgb(17, 19, 30);
        public static readonly Color Border = Color.FromArgb(54, 58, 84);
        public static readonly Color NavSelected = Color.FromArgb(40, 43, 64);
        public static readonly Color NavHover = Color.FromArgb(31, 34, 51);
        public static readonly Color Selected = Color.FromArgb(46, 48, 88);
        public static readonly Color Green = Color.FromArgb(35, 165, 90);
        public static readonly Color GreenLight = Color.FromArgb(59, 201, 118);
        public static readonly Color Red = Color.FromArgb(242, 63, 66);
        public static readonly Color RedDark = Color.FromArgb(218, 55, 60);
        public static readonly Color Yellow = Color.FromArgb(240, 178, 50);
        public static readonly Color Orange = Color.FromArgb(245, 140, 45); // connected, but the connection is poor
        public static readonly Color OrangeDark = Color.FromArgb(214, 110, 25);
        public static readonly Color Slate = Color.FromArgb(52, 56, 80);
        public static readonly Color SlateLight = Color.FromArgb(74, 79, 108);
        public static readonly Color Map = Color.FromArgb(78, 88, 170);

        static float ScreenScale()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        // Sizes are written for 96 DPI and scaled here; fonts are in points, which Windows scales itself.
        public static int S(float value) { return (int)Math.Round(value * Scale); }

        public static Color Shade(Color c, int amount)
        {
            return Color.FromArgb(c.A, Clamp(c.R + amount), Clamp(c.G + amount), Clamp(c.B + amount));
        }

        static int Clamp(int v) { return Math.Max(0, Math.Min(255, v)); }
    }

    public static class Fonts
    {
        // Windows 11 has Segoe Fluent Icons, Windows 10 has Segoe MDL2 Assets; both use the same code points.
        static readonly string IconFamily = FindIconFont();

        public static readonly Font Brand = new Font("Segoe UI", 24f, FontStyle.Bold);
        public static readonly Font Tagline = new Font("Segoe UI", 10.5f);
        public static readonly Font Nav = new Font("Segoe UI", 11.5f);
        public static readonly Font NavSelected = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font NavIcon = Icons(14f);
        public static readonly Font HeroTitle = new Font("Segoe UI", 24f, FontStyle.Bold);
        public static readonly Font Badge = new Font("Segoe UI Semibold", 9.75f);
        public static readonly Font MiniLogTitle = new Font("Segoe UI", 8f, FontStyle.Bold);
        public static readonly Font MiniLog = new Font("Consolas", 8.75f);
        public static readonly Font HeroText = new Font("Segoe UI", 12f);
        public static readonly Font Button = new Font("Segoe UI Semibold", 15f);
        public static readonly Font ButtonIcon = Icons(15f);
        public static readonly Font Outline = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font OutlineIcon = Icons(10.5f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 11.5f);
        public static readonly Font CardText = new Font("Segoe UI", 10f);
        public static readonly Font CardIcon = Icons(22f);
        public static readonly Font AvatarIcon = Icons(16f);
        public static readonly Font PageTitle = new Font("Segoe UI Semibold", 15f);
        public static readonly Font PageTitleIcon = Icons(15f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);
        public static readonly Font SidebarHeader = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        public static readonly Font Category = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        public static readonly Font Presence = new Font("Segoe UI Semibold", 10f);
        public static readonly Font PresenceText = new Font("Segoe UI", 8.75f);
        public static readonly Font Notice = new Font("Segoe UI Semibold", 10f);

        static string FindIconFont()
        {
            foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                using (var font = new Font(name, 10f))
                    if (font.Name == name) return name;
            return "Segoe UI Symbol";
        }

        static Font Icons(float size) { return new Font(IconFamily, size); }
    }

    public static class Glyph
    {
        public const string Home = "";
        public const string Globe = "";
        public const string Settings = "";
        public const string Log = "";
        public const string Info = "";
        public const string Key = "";
        public const string Play = "";
        public const string Stop = "";
        public const string Copy = "";
        public const string Open = "";
        public const string People = "";
        public const string Add = "";
        public const string Delete = "";
        public const string Apps = "\uE71D";
        public const string Folder = "\uE8B7";
        public const string Speed = "\uEC4A";
        public const string Refresh = "\uE72C";
    }

    public static class Art
    {
        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // The Nevatch shield, filling r (looks best at about 0.86 wide : 1 high).
        public static GraphicsPath ShieldPath(RectangleF r)
        {
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            var p = new GraphicsPath();
            p.AddBezier(x + w * 0.5f, y, x + w * 0.66f, y + h * 0.09f, x + w * 0.84f, y + h * 0.12f, x + w, y + h * 0.13f);
            p.AddLine(x + w, y + h * 0.13f, x + w, y + h * 0.46f);
            p.AddBezier(x + w, y + h * 0.46f, x + w, y + h * 0.76f, x + w * 0.74f, y + h * 0.91f, x + w * 0.5f, y + h);
            p.AddBezier(x + w * 0.5f, y + h, x + w * 0.26f, y + h * 0.91f, x, y + h * 0.76f, x, y + h * 0.46f);
            p.AddLine(x, y + h * 0.46f, x, y + h * 0.13f);
            p.AddBezier(x, y + h * 0.13f, x + w * 0.16f, y + h * 0.12f, x + w * 0.34f, y + h * 0.09f, x + w * 0.5f, y);
            p.CloseFigure();
            return p;
        }

        public static void DrawShield(Graphics g, RectangleF r, Color light, Color dark)
        {
            using (GraphicsPath shield = ShieldPath(r))
            {
                using (var fill = new LinearGradientBrush(r, light, dark, 65f)) g.FillPath(fill, shield);
                // The right half a little darker, like a folded metal badge.
                using (var region = new Region(shield))
                using (var shade = new SolidBrush(Color.FromArgb(40, 0, 20, 90)))
                {
                    region.Intersect(new RectangleF(r.X + r.Width / 2, r.Y, r.Width / 2 + 1, r.Height));
                    g.FillRegion(shade, region);
                }
            }
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height * 0.40f, radius = r.Width * 0.15f;
            using (var white = new SolidBrush(Color.White))
            {
                g.FillEllipse(white, cx - radius, cy - radius, radius * 2, radius * 2);
                float stem = radius * 0.92f;
                using (GraphicsPath p = RoundRect(new RectangleF(cx - stem / 2, cy, stem, r.Height * 0.27f), stem * 0.4f))
                    g.FillPath(white, p);
            }
            using (var hole = new SolidBrush(dark))
            {
                float h = radius * 0.4f;
                g.FillEllipse(hole, cx - h, cy - h, h * 2, h * 2);
            }
        }

        public static Bitmap ShieldBitmap(int size) { return ShieldBitmap(size, false); }

        // green: the "on" look (connected, or someone is connected), as on the home page.
        public static Bitmap ShieldBitmap(int size, bool green)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Smooth(g);
                float h = size * 0.96f, w = h * 0.86f;
                DrawShield(g, new RectangleF((size - w) / 2, (size - h) / 2, w, h),
                    green ? Theme.GreenLight : Theme.AccentLight, green ? Theme.Green : Theme.AccentDark);
            }
            return bmp;
        }

        public static Icon ShieldIcon(int size) { return ShieldIcon(size, false); }

        public static Icon ShieldIcon(int size, bool green)
        {
            using (Bitmap bmp = ShieldBitmap(size, green)) return Icon.FromHandle(bmp.GetHicon());
        }

        // Writes a classic multi-size .ico (32-bit bitmaps), which csc's /win32icon accepts.
        public static void SaveIco(string path, int[] sizes)
        {
            var images = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (Bitmap bmp = ShieldBitmap(size))
                using (var ms = new MemoryStream())
                using (var w = new BinaryWriter(ms))
                {
                    int maskRow = ((size + 31) / 32) * 4;
                    w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
                    w.Write(0); w.Write(size * size * 4 + maskRow * size); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    BitmapData data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var row = new byte[size * 4];
                    for (int y = size - 1; y >= 0; y--)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                        w.Write(row);
                    }
                    bmp.UnlockBits(data);
                    w.Write(new byte[maskRow * size]);
                    w.Flush();
                    images.Add(ms.ToArray());
                }
            }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) w.Write(image);
            }
        }

        // A rough world map: 64 columns (longitude -180..180) by 25 rows (latitude 80..-55),
        // each row listing the land columns. Drawn as a light dot pattern behind the status.
        static readonly string[] MapRows = {
            "21-28 34-35 43-50",
            "11-19 22-28 44-56",
            "3-7 8-21 23-27 28-29 33-37 38-63",
            "4-7 8-14 18-21 24-25 33-37 38-61",
            "9-15 18-21 31 33-39 40-56 60",
            "10-22 31-32 33-39 40-56",
            "10-20 31-39 40-56 57",
            "10-18 30-32 33-36 37-40 41-55 56",
            "11-17 30-37 38-42 43-53",
            "12-14 17 29-35 38-42 44-53",
            "13-14 17-18 29-37 39-42 44-47 48-51",
            "15-16 29-38 45-46 49-51 53",
            "17 29-40 45 49-51 54",
            "18-21 30-39 49-50",
            "18-23 34-39 49-52",
            "18-25 34-38 50-56",
            "18-25 34-38 53-58",
            "18-24 34-38 40 54-57",
            "19-24 34-37 40 52-58",
            "19-23 34-37 52-59",
            "19-22 35-37 52-58",
            "19-21 56-58 63",
            "19-20 57 62",
            "19-20",
            "19",
        };

        public static void DrawWorldMap(Graphics g, RectangleF area, Color color)
        {
            const int cols = 64;
            int rows = MapRows.Length;
            float cell = Math.Min(area.Width / cols, area.Height / rows);
            float left = area.X + (area.Width - cell * cols) / 2, top = area.Y + (area.Height - cell * rows) / 2;
            float half = cell / 2, dot = cell * 0.28f;
            var random = new Random(11);
            var brushes = new SolidBrush[4];
            for (int i = 0; i < brushes.Length; i++) brushes[i] = new SolidBrush(Color.FromArgb(80 + i * 32, color));
            try
            {
                for (int row = 0; row < rows; row++)
                {
                    foreach (string range in MapRows[row].Split(' '))
                    {
                        string[] ends = range.Split('-');
                        int from = int.Parse(ends[0]), to = int.Parse(ends[ends.Length - 1]);
                        for (int c = from; c <= to; c++)
                            for (int sy = 0; sy < 2; sy++)
                                for (int sx = 0; sx < 2; sx++)
                                    g.FillEllipse(brushes[random.Next(brushes.Length)],
                                        left + c * cell + sx * half + (half - dot) / 2,
                                        top + row * cell + sy * half + (half - dot) / 2, dot, dot);
                    }
                }
            }
            finally
            {
                foreach (SolidBrush b in brushes) b.Dispose();
            }
        }
    }

    // Dark title bar, dark scroll bars and list headers, where Windows supports them (10 1809+ / 11).
    public static class Dark
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        public static void TitleBar(Form form)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref on, 4);
                int caption = Theme.Rail.R | Theme.Rail.G << 8 | Theme.Rail.B << 16; // COLORREF; Windows 11 only
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
            }
            catch { }
        }

        // Scroll bars (and a list view's header) in the dark style, now or once the control has its window.
        public static void Controls(Control c)
        {
            if (c.IsHandleCreated) Apply(c);
            c.HandleCreated += (s, e) => Apply(c);
        }

        // Windows only draws "DarkMode_Explorer" scroll bars dark once the app and the window allow dark mode;
        // otherwise they stay light grey. These are uxtheme's unnamed exports (by ordinal), present on 10 1809+ / 11.
        [DllImport("uxtheme.dll", EntryPoint = "#135")]
        static extern int SetPreferredAppMode(int mode); // 1809: AllowDarkModeForApp(bool); 2 = force dark (true)
        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        static extern void FlushMenuThemes();

        static bool appDark;

        static void AllowForApp()
        {
            if (appDark) return;
            appDark = true;
            try
            {
                SetPreferredAppMode(2);
                FlushMenuThemes();
            }
            catch { }
        }

        static void Apply(Control c)
        {
            AllowForApp();
            try { AllowDarkModeForWindow(c.Handle, true); } catch { }
            try
            {
                SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch { }
        }

        // A small standard dialog in the dark look: call after its controls are added.
        public static void Dialog(Form form)
        {
            form.BackColor = Theme.ContentBg;
            form.ForeColor = Theme.Text;
            form.HandleCreated += (s, e) => TitleBar(form);
            foreach (Control c in form.Controls)
            {
                var box = c as TextBox;
                if (box != null)
                {
                    box.BackColor = Theme.InputBg;
                    box.ForeColor = Theme.Text;
                    box.BorderStyle = BorderStyle.FixedSingle;
                    Controls(box);
                }
                var button = c as Button;
                if (button != null)
                {
                    bool main = form.AcceptButton == button;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.BackColor = main ? Theme.Accent : Theme.Slate;
                    button.FlatAppearance.MouseOverBackColor = main ? Theme.AccentDark : Theme.SlateLight;
                    button.ForeColor = Color.White;
                    button.Cursor = Cursors.Hand;
                }
            }
        }
    }

    public static class StartWithWindows
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        static string Command() { return "\"" + Application.ExecutablePath + "\" --minimized"; }

        public static bool IsOn(string name)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && string.Equals(key.GetValue(name) as string, Command(), StringComparison.OrdinalIgnoreCase);
        }

        public static void Set(string name, bool on)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue(name, Command(), RegistryValueKind.String);
                else key.DeleteValue(name, false);
            }
        }
    }

    // ---------- the window ----------

    // Sidebar on the left (logo, name, tagline, pages), the selected page on the right.
    // Closing the window hides it next to the clock; Quit (tray menu) really closes it.
    public class ShellForm : Form
    {
        readonly Sidebar sidebar;
        readonly NoticeBar notice;
        readonly Panel content = new Panel();
        readonly List<NavItem> navItems = new List<NavItem>();
        readonly List<Control> pages = new List<Control>();
        protected readonly NotifyIcon Tray = new NotifyIcon();
        protected string TrayHint = "It keeps running next to the clock. Right-click the shield there and choose Quit to close it.";
        protected bool Quitting;
        bool trayHintShown;
        readonly Icon[] trayIcons = new Icon[2]; // blue, green

        // The icon next to the clock turns green while connected (Nevatch) or while someone is connected (server).
        protected bool TrayGreen
        {
            set { Tray.Icon = trayIcons[value ? 1 : 0]; }
        }

        protected ShellForm(string windowTitle, string brand, string tagline)
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9.75f);
            Text = windowTitle;
            Icon = Art.ShieldIcon(Theme.S(32));
            BackColor = Theme.ContentBg;
            ClientSize = new Size(Theme.S(920), Theme.S(660));
            MinimumSize = SizeFromClientSize(new Size(Theme.S(800), Theme.S(600)));
            StartPosition = FormStartPosition.CenterScreen;

            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.ContentBg;
            sidebar = new Sidebar(brand, tagline) { Dock = DockStyle.Left, Width = Theme.S(220) };
            notice = new NoticeBar { Dock = DockStyle.Top, Height = Theme.S(38), Visible = false };
            // Docked last-added first: the sidebar takes the left edge, the notice the top of what's left.
            Controls.Add(content);
            Controls.Add(notice);
            Controls.Add(sidebar);

            Tray.Icon = trayIcons[0] = Art.ShieldIcon(SystemInformation.SmallIconSize.Width);
            trayIcons[1] = Art.ShieldIcon(SystemInformation.SmallIconSize.Width, true);
            Tray.Text = windowTitle;
            Tray.Visible = true;
            Tray.DoubleClick += (s, e) => ShowFromTray();
            ResumeLayout(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Dark.TitleBar(this);
        }

        // A colored bar across the top of the page area, like Discord's notices; button may be null.
        protected void ShowNotice(Color color, string text, string button, Action onButton)
        {
            notice.Set(color, text, button, onButton);
            notice.Visible = true;
        }

        protected void HideNotice() { notice.Visible = false; }

        // Nevatch's own popup in the corner of the screen (instead of Windows' balloon); clicking it opens the window.
        protected void Notify(string title, string text, ToastKind kind, int milliseconds)
        {
            try { Toast.Popup(title, text, kind, milliseconds, ShowFromTray); }
            catch { }
        }

        // The panel at the bottom of the sidebar: a status dot on the logo and two lines of text.
        protected void SetPresence(Color dot, string title, string text)
        {
            sidebar.SetPresence(dot, title, text);
        }

        protected void AddPage(string glyph, string title, Control page)
        {
            int index = pages.Count;
            var item = new NavItem(glyph, title);
            item.Click += (s, e) => ShowPage(index);
            sidebar.Add(item);
            navItems.Add(item);
            var withHeading = page as Page;
            if (withHeading != null) withHeading.HeadingGlyph = glyph;
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            content.Controls.Add(page);
            pages.Add(page);
            if (index == 0) ShowPage(0);
        }

        public void ShowPage(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navItems[i].Selected = i == index;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            Tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected void HideToTray(bool hint)
        {
            Hide();
            if (hint && !trayHintShown)
            {
                Notify(Text + " is still running", TrayHint, ToastKind.Info, 6000);
                trayHintShown = true;
            }
        }

        public void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected void Quit()
        {
            Quitting = true;
            Close();
        }
    }

    // The list of pages under a small category label, and a health card at the bottom.
    class Sidebar : Panel
    {
        static int RailWidth { get { return 0; } } // no logo rail any more
        static int HeaderHeight { get { return Theme.S(4); } } // no name header any more, just a little room at the top
        static int PresenceHeight { get { return Theme.S(60); } }

        readonly string brand, tagline;
        readonly List<NavItem> items = new List<NavItem>();
        Color presenceDot = Theme.SlateLight;
        string presenceTitle, presenceText;

        public Sidebar(string brand, string tagline)
        {
            this.brand = brand;
            this.tagline = tagline;
            presenceTitle = brand;
            presenceText = tagline;
            BackColor = Theme.SidebarBg;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public void Add(NavItem item)
        {
            items.Add(item);
            Controls.Add(item);
            PerformLayout();
        }

        public void SetPresence(Color dot, string title, string text)
        {
            presenceDot = dot;
            presenceTitle = title ?? brand;
            presenceText = text ?? "";
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = RailWidth + Theme.S(8), y = HeaderHeight + Theme.S(40);
            foreach (NavItem item in items)
            {
                item.SetBounds(left, y, Width - left - Theme.S(8), Theme.S(38));
                y += Theme.S(42);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Art.Smooth(g);
            int rail = RailWidth;

            TextRenderer.DrawText(g, tagline.ToUpperInvariant(), Fonts.Category,
                new Rectangle(rail + Theme.S(16), HeaderHeight + Theme.S(16), Width - rail - Theme.S(24), Theme.S(18)), Theme.NavText,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            // Health card at the bottom: one solid card, a bar down its left edge in the health color
            // (green good, orange on the edge, red bad), and the status title and line next to it.
            int top = Height - PresenceHeight;
            int inset = Theme.S(8);
            var card = new RectangleF(rail + inset, top + inset / 2f, Width - rail - inset * 2, PresenceHeight - inset * 1.5f);
            using (GraphicsPath p = Art.RoundRect(card, Theme.S(8)))
            using (var b = new SolidBrush(Theme.PresenceBg)) g.FillPath(b, p);
            float barW = Theme.S(4), barPad = Theme.S(9);
            using (GraphicsPath p = Art.RoundRect(new RectangleF(card.X + Theme.S(8), card.Y + barPad, barW, card.Height - barPad * 2), barW / 2))
            using (var b = new SolidBrush(presenceDot)) g.FillPath(b, p);
            int textLeft = (int)(card.X + Theme.S(8) + barW + Theme.S(10)), textW = Math.Max(10, (int)card.Right - textLeft - Theme.S(8));
            int line1 = TextRenderer.MeasureText("Ag", Fonts.Presence, new Size(1000, 100), TextFormatFlags.NoPadding).Height;
            int line2 = TextRenderer.MeasureText("Ag", Fonts.PresenceText, new Size(1000, 100), TextFormatFlags.NoPadding).Height;
            int textTop = (int)(card.Y + (card.Height - line1 - line2) / 2);
            const TextFormatFlags one = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, presenceTitle, Fonts.Presence, new Rectangle(textLeft, textTop, textW, line1), Theme.Text, one);
            TextRenderer.DrawText(g, presenceText, Fonts.PresenceText, new Rectangle(textLeft, textTop + line1, textW, line2), Theme.NavText, one);
        }
    }

    public enum ToastKind { Info, Success, Warning, Error }

    // Nevatch's own notification instead of Windows' balloon: a dark card in the bottom-right corner with the logo,
    // a colored accent, a title and text, and a bar running down until it closes. Fades in and out, waits while
    // the mouse is on it, and clicking it runs onClick (usually: open the window).
    public class Toast : Form
    {
        static Toast current;
        readonly string title, text;
        readonly Color accent;
        readonly Action onClick;
        readonly Timer timer = new Timer { Interval = 15 };
        readonly int duration;
        int elapsed;
        bool closing, overClose;
        Rectangle closeBox;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void Popup(string title, string text, ToastKind kind, int milliseconds, Action onClick)
        {
            if (current != null && !current.IsDisposed) current.Close();
            current = new Toast(title, text, kind, milliseconds, onClick);
            current.Show();
        }

        Toast(string title, string text, ToastKind kind, int milliseconds, Action onClick)
        {
            this.title = title ?? "";
            this.text = text ?? "";
            this.onClick = onClick;
            duration = Math.Max(2000, milliseconds);
            accent = kind == ToastKind.Success ? Theme.Green : kind == ToastKind.Warning ? Theme.Yellow :
                     kind == ToastKind.Error ? Theme.Red : Theme.Accent;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
            Opacity = 0;
            Cursor = Cursors.Hand;

            int width = Theme.S(380), textLeft = Theme.S(76), textW = width - textLeft - Theme.S(40);
            int titleH = TextRenderer.MeasureText(this.title, Fonts.CardTitle, new Size(textW, 1000), TextFormatFlags.NoPadding).Height;
            int bodyH = TextRenderer.MeasureText(this.text, Fonts.CardText, new Size(textW, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            int height = Math.Max(Theme.S(84), Theme.S(18) + titleH + Theme.S(4) + bodyH + Theme.S(22));
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Bounds = new Rectangle(area.Right - width - Theme.S(16), area.Bottom - height - Theme.S(16), width, height);

            timer.Tick += (s, e) => Step();
        }

        // Shown without taking the focus from whatever the person is doing, and kept out of Alt+Tab.
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80 | 0x08000000; // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } // rounded corners on Windows 11
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            timer.Start();
        }

        void Step()
        {
            if (closing)
            {
                Opacity = Math.Max(0, Opacity - 0.08);
                if (Opacity <= 0) { timer.Stop(); Close(); }
                return;
            }
            if (Opacity < 1) Opacity = Math.Min(1, Opacity + 0.1);
            // Waits while the mouse is on it.
            if (!Bounds.Contains(Cursor.Position)) elapsed += timer.Interval;
            if (elapsed >= duration) closing = true;
            Invalidate(new Rectangle(0, Height - Theme.S(4), Width, Theme.S(4)));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = closeBox.Contains(e.Location);
            if (over != overClose) { overClose = over; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { overClose = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (!closeBox.Contains(e.Location) && onClick != null) onClick();
            closing = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Dispose();
            if (current == this) current = null;
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.CardBg);
            Art.Smooth(g);
            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, Theme.S(4), Height);

            // The logo in a blurple tile, with a dot in the toast's color.
            float tile = Theme.S(40), tx = Theme.S(22), ty = Theme.S(18);
            using (GraphicsPath p = Art.RoundRect(new RectangleF(tx, ty, tile, tile), Theme.S(12)))
            using (var b = new LinearGradientBrush(new RectangleF(tx, ty, tile, tile), Theme.AccentLight, Theme.AccentDark, 70f))
                g.FillPath(b, p);
            float sh = tile * 0.62f, sw = sh * 0.86f;
            Art.DrawShield(g, new RectangleF(tx + (tile - sw) / 2, ty + (tile - sh) / 2, sw, sh), Color.White, Color.FromArgb(214, 218, 255));
            float dot = Theme.S(14), ring = Theme.S(3);
            using (var b = new SolidBrush(Theme.CardBg)) g.FillEllipse(b, tx + tile - dot + ring, ty + tile - dot + ring, dot, dot);
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, tx + tile - dot + ring * 2, ty + tile - dot + ring * 2, dot - ring * 2, dot - ring * 2);

            int textLeft = Theme.S(76), textW = Width - textLeft - Theme.S(40), y = Theme.S(18);
            int titleH = TextRenderer.MeasureText(title, Fonts.CardTitle, new Size(textW, 1000), TextFormatFlags.NoPadding).Height;
            TextRenderer.DrawText(g, title, Fonts.CardTitle, new Rectangle(textLeft, y, textW, titleH), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, text, Fonts.CardText, new Rectangle(textLeft, y + titleH + Theme.S(4), textW, Height - y - titleH - Theme.S(12)),
                Theme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            closeBox = new Rectangle(Width - Theme.S(36), Theme.S(10), Theme.S(26), Theme.S(26));
            if (overClose)
                using (GraphicsPath p = Art.RoundRect(closeBox, Theme.S(6)))
                using (var b = new SolidBrush(Theme.NavHover)) g.FillPath(b, p);
            using (var pen = new Pen(overClose ? Theme.Text : Theme.NavText, Math.Max(1.5f, Theme.Scale * 1.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float c = Theme.S(5), cx = closeBox.X + closeBox.Width / 2f, cy = closeBox.Y + closeBox.Height / 2f;
                g.DrawLine(pen, cx - c, cy - c, cx + c, cy + c);
                g.DrawLine(pen, cx - c, cy + c, cx + c, cy - c);
            }

            // Time left, as a thin bar along the bottom.
            float left = 1f - Math.Min(1f, elapsed / (float)duration);
            using (var b = new SolidBrush(Color.FromArgb(160, accent)))
                g.FillRectangle(b, Theme.S(4), Height - Theme.S(3), (Width - Theme.S(4)) * left, Theme.S(3));
        }
    }

    // The bar across the top of the page area for problems and news; the button is drawn inside it.
    class NoticeBar : PaintedControl
    {
        Color color = Theme.RedDark;
        string button;
        Action onButton;
        Rectangle buttonBox;

        public void Set(Color color, string text, string button, Action onButton)
        {
            this.color = color;
            this.button = button;
            this.onButton = onButton;
            Text = text ?? "";
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = buttonBox.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && onButton != null && buttonBox.Contains(e.Location)) onButton();
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(color);
            Art.Smooth(g);
            int pad = Theme.S(16), buttonW = 0, gap = 0;
            if (!string.IsNullOrEmpty(button))
            {
                buttonW = TextRenderer.MeasureText(button, Fonts.Notice).Width + Theme.S(16);
                gap = Theme.S(14);
            }
            int textMax = Math.Max(10, Width - pad * 2 - buttonW - gap);
            int textW = Math.Min(textMax, TextRenderer.MeasureText(Text, Fonts.Notice).Width);
            int x = Math.Max(pad, (Width - textW - gap - buttonW) / 2);
            TextRenderer.DrawText(g, Text, Fonts.Notice, new Rectangle(x, 0, textW, Height), Color.White,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            buttonBox = Rectangle.Empty;
            if (buttonW > 0)
            {
                int bh = Theme.S(24);
                buttonBox = new Rectangle(x + textW + gap, (Height - bh) / 2, buttonW, bh);
                bool over = Hover && buttonBox.Contains(PointToClient(Cursor.Position));
                using (GraphicsPath p = Art.RoundRect(buttonBox, Theme.S(4)))
                {
                    if (over) using (var b = new SolidBrush(Color.FromArgb(40, Color.White))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.White, Math.Max(1f, Theme.Scale))) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, button, Fonts.Notice, buttonBox, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // ---------- controls ----------

    public class PaintedControl : Control
    {
        protected bool Hover, Down;

        public PaintedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        protected Color Behind { get { return Parent != null ? Parent.BackColor : Theme.ContentBg; } }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    public class NavItem : PaintedControl
    {
        readonly string glyph;
        bool selected;

        public NavItem(string glyph, string text)
        {
            this.glyph = glyph;
            Text = text;
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Behind);
            Art.Smooth(g);
            if (selected || Hover)
            {
                using (GraphicsPath p = Art.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(6)))
                using (var b = new SolidBrush(selected ? Theme.NavSelected : Theme.NavHover))
                    g.FillPath(b, p);
            }
            Color color = selected ? Theme.Text : Hover ? Theme.Muted : Theme.NavText;
            TextRenderer.DrawText(g, glyph, Fonts.NavIcon, new Rectangle(Theme.S(8), 0, Theme.S(28), Height), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Text, selected ? Fonts.NavSelected : Fonts.Nav, new Rectangle(Theme.S(44), 0, Width - Theme.S(48), Height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }
    }

    // The big rounded call-to-action button (Connect / Start server).
    public class PillButton : PaintedControl
    {
        string glyph;
        Color light = Theme.AccentLight, dark = Theme.AccentDark;

        public PillButton() { Cursor = Cursors.Hand; }

        public void SetLook(string glyph, string text, Color light, Color dark)
        {
            this.glyph = glyph;
            this.light = light;
            this.dark = dark;
            Text = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Behind);
            Art.Smooth(g);
            var r = new RectangleF(Theme.S(8), Theme.S(2), Width - Theme.S(16), Height - Theme.S(16));
            Color top = Enabled ? light : Theme.Faint, bottom = Enabled ? dark : Theme.Shade(Theme.Faint, -18);
            if (Enabled)
            {
                // One soft colored shadow under the button.
                for (int i = 2; i >= 1; i--)
                {
                    var shadow = new RectangleF(r.X + Theme.S(6 * i), r.Y + Theme.S(2 + 3 * i), r.Width - Theme.S(12 * i), r.Height);
                    using (GraphicsPath p = Art.RoundRect(shadow, Theme.S(14)))
                    using (var b = new SolidBrush(Color.FromArgb(28, dark)))
                        g.FillPath(b, p);
                }
                if (Down) { top = Theme.Shade(top, -22); bottom = Theme.Shade(bottom, -22); }
                else if (Hover) { top = Theme.Shade(top, 14); bottom = Theme.Shade(bottom, 14); }
            }
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(14)))
            using (var b = new LinearGradientBrush(r, top, bottom, 90f))
                g.FillPath(b, p);

            SizeF textSize = g.MeasureString(Text, Fonts.Button);
            float icon = string.IsNullOrEmpty(glyph) ? 0 : Theme.S(18);
            float gap = icon > 0 ? Theme.S(14) : 0;
            float x = r.X + (r.Width - icon - gap - textSize.Width) / 2;
            float mid = r.Y + r.Height / 2;
            using (var white = new SolidBrush(Color.White))
            {
                if (icon > 0) DrawIcon(g, white, new RectangleF(x, mid - icon / 2, icon, icon));
                g.DrawString(Text, Fonts.Button, white, x + icon + gap, mid - textSize.Height / 2);
            }
        }

        // Solid play triangle / stop square; any other glyph comes from the icon font.
        void DrawIcon(Graphics g, Brush brush, RectangleF box)
        {
            if (glyph == Glyph.Play)
            {
                using (var p = new GraphicsPath())
                using (var pen = new Pen(brush, Theme.S(3)) { LineJoin = LineJoin.Round })
                {
                    float inset = Theme.S(2);
                    p.AddPolygon(new[] {
                        new PointF(box.X + inset, box.Y + inset),
                        new PointF(box.Right - inset, box.Y + box.Height / 2),
                        new PointF(box.X + inset, box.Bottom - inset) });
                    g.FillPath(brush, p);
                    g.DrawPath(pen, p);
                }
            }
            else if (glyph == Glyph.Stop)
            {
                float inset = Theme.S(1.5f);
                var square = new RectangleF(box.X + inset, box.Y + inset, box.Width - inset * 2, box.Height - inset * 2);
                using (GraphicsPath p = Art.RoundRect(square, Theme.S(4))) g.FillPath(brush, p);
            }
            else
            {
                SizeF size = g.MeasureString(glyph, Fonts.ButtonIcon);
                g.DrawString(glyph, Fonts.ButtonIcon, brush, box.X + (box.Width - size.Width) / 2, box.Y + (box.Height - size.Height) / 2);
            }
        }
    }

    public class OutlineButton : PaintedControl
    {
        public bool Primary; // the calmer accent color instead of grey (Add key on the home card)

        public OutlineButton() { Cursor = Cursors.Hand; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Behind);
            Art.Smooth(g);
            // Discord's filled grey "secondary" button.
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Color baseFill = Primary ? Theme.AccentSoft : Theme.Slate;
            Color fill = !Enabled ? Theme.Shade(Theme.Slate, -14) : Down ? Theme.Shade(baseFill, -12) : Hover ? Theme.Shade(baseFill, 16) : baseFill;
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(8)))
            using (var b = new SolidBrush(fill))
                g.FillPath(b, p);
            TextRenderer.DrawText(g, Text, Fonts.Outline, ClientRectangle, Enabled ? Color.White : Theme.NavText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // A white rounded card; children sit on white.
    public class Card : Panel
    {
        public Card()
        {
            BackColor = Theme.CardBg;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.ContentBg);
            Art.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(10)))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.CardBorder, Math.Max(1f, Theme.Scale))) g.DrawPath(pen, p);
            }
        }
    }

    // Card with an icon, a title, some text and optionally a button on the right.
    public class InfoCard : Card
    {
        readonly string glyph;
        string title = "", body = "";
        bool bodyIsError;
        public readonly OutlineButton Button;

        public InfoCard(string glyph, string buttonText)
        {
            this.glyph = glyph;
            if (buttonText != null)
            {
                Button = new OutlineButton { Text = buttonText };
                Controls.Add(Button);
            }
        }

        public void Set(string title, string body) { Set(title, body, false); }

        public void Set(string title, string body, bool bodyIsError)
        {
            this.title = title ?? "";
            this.body = body ?? "";
            this.bodyIsError = bodyIsError;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Button != null)
                Button.SetBounds(Width - Theme.S(24) - Theme.S(170), (Height - Theme.S(46)) / 2, Theme.S(170), Theme.S(46));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int left = Theme.S(26);
            if (glyph != null)
            {
                // The icon in a round "avatar", Discord style.
                Art.Smooth(g);
                int av = Theme.S(48), ax = Theme.S(22), ay = (Height - av) / 2;
                using (var b = new SolidBrush(bodyIsError ? Color.FromArgb(70, Theme.Red) : Theme.Selected)) g.FillEllipse(b, ax, ay, av, av);
                TextRenderer.DrawText(g, glyph, Fonts.AvatarIcon, new Rectangle(ax, ay, av, av),
                    bodyIsError ? Theme.Red : Color.FromArgb(201, 205, 251),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                left = Theme.S(88);
            }
            int right = Button != null ? Button.Left - Theme.S(20) : Width - Theme.S(26);
            int width = Math.Max(10, right - left);
            const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            int titleHeight = TextRenderer.MeasureText(g, "Ag", Fonts.CardTitle, new Size(width, 1000), TextFormatFlags.NoPadding).Height;
            int bodyHeight = body.Length == 0 ? 0 : TextRenderer.MeasureText(g, body, Fonts.CardText, new Size(width, 1000), wrap).Height;
            int gap = body.Length == 0 ? 0 : Theme.S(4);
            int y = Math.Max(Theme.S(12), (Height - titleHeight - gap - bodyHeight) / 2);
            TextRenderer.DrawText(g, title, Fonts.CardTitle, new Rectangle(left, y, width, titleHeight), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, body, Fonts.CardText, new Rectangle(left, y + titleHeight + gap, width, Height - y - titleHeight - gap - Theme.S(8)),
                bodyIsError ? Theme.Red : Theme.Muted, wrap | TextFormatFlags.EndEllipsis);
        }
    }

    // A settings row: title and description on the left, an on/off switch on the right.
    public class Toggle : PaintedControl
    {
        readonly string description;
        bool on;
        public event EventHandler CheckedChanged;

        public Toggle(string title, string description)
        {
            Text = title;
            this.description = description;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return on; }
            set { if (on == value) return; on = value; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            on = !on;
            Invalidate();
            EventHandler handler = CheckedChanged;
            if (handler != null) handler(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Behind);
            Art.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(10)))
            {
                using (var b = new SolidBrush(Hover ? Theme.CardHover : Theme.CardBg)) g.FillPath(b, p);
                using (var pen = new Pen(Theme.CardBorder, Math.Max(1f, Theme.Scale))) g.DrawPath(pen, p);
            }

            // Discord's switch: green with a check when on, grey with a cross when off.
            int trackW = Theme.S(44), trackH = Theme.S(26);
            var track = new RectangleF(Width - Theme.S(26) - trackW, (Height - trackH) / 2f, trackW, trackH);
            using (GraphicsPath p = Art.RoundRect(track, trackH / 2f))
            using (var b = new SolidBrush(on ? Theme.Green : Color.FromArgb(128, 132, 142)))
                g.FillPath(b, p);
            float knob = trackH - Theme.S(6);
            float knobX = on ? track.Right - Theme.S(3) - knob : track.X + Theme.S(3), knobY = track.Y + Theme.S(3);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, knobX, knobY, knob, knob);
            using (var pen = new Pen(on ? Theme.Green : Color.FromArgb(128, 132, 142), Math.Max(1.5f, Theme.Scale * 1.8f))
                { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = knobX + knob / 2, cy = knobY + knob / 2, k = knob * 0.2f;
                if (on) g.DrawLines(pen, new[] { new PointF(cx - k, cy), new PointF(cx - k * 0.2f, cy + k * 0.8f), new PointF(cx + k * 1.1f, cy - k * 0.8f) });
                else { g.DrawLine(pen, cx - k, cy - k, cx + k, cy + k); g.DrawLine(pen, cx - k, cy + k, cx + k, cy - k); }
            }

            int left = Theme.S(26), width = (int)track.X - left - Theme.S(20);
            int titleH = TextRenderer.MeasureText(g, "Ag", Fonts.CardTitle, new Size(width, 1000), TextFormatFlags.NoPadding).Height;
            int descH = TextRenderer.MeasureText(g, description, Fonts.CardText, new Size(width, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
            int y = (Height - titleH - Theme.S(4) - descH) / 2;
            TextRenderer.DrawText(g, Text, Fonts.CardTitle, new Rectangle(left, y, width, titleH), Theme.Text, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, description, Fonts.CardText, new Rectangle(left, y + titleH + Theme.S(4), width, descH), Theme.Muted,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }
    }

    // A row in a pick-one list: a round marker, title and text, and a remove button on the right.
    // Click picks the row; RemoveClicked fires for the remove button. Marker and remove button are optional.
    public class ChoiceRow : PaintedControl
    {
        readonly string body;
        readonly bool marker, removable;
        bool selected, overRemove;
        public event EventHandler RemoveClicked;

        public ChoiceRow(string title, string body, bool selected) : this(title, body, selected, true, true) { }

        public ChoiceRow(string title, string body, bool selected, bool marker, bool removable)
        {
            Text = title;
            this.body = body ?? "";
            this.selected = selected;
            this.marker = marker;
            this.removable = removable;
            Cursor = marker ? Cursors.Hand : Cursors.Default;
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }

        Rectangle RemoveBox
        {
            get
            {
                if (!removable) return Rectangle.Empty;
                int size = Theme.S(40);
                return new Rectangle(Width - Theme.S(18) - size, (Height - size) / 2, size, size);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool over = RemoveBox.Contains(e.Location);
            if (over != overRemove)
            {
                overRemove = over;
                Cursor = over || marker ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { overRemove = false; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && RemoveBox.Contains(e.Location))
            {
                EventHandler handler = RemoveClicked;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.OnMouseClick(e);
        }

        // Clicks on the remove button shouldn't also pick the row.
        protected override void OnClick(EventArgs e)
        {
            if (!overRemove) base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Behind);
            Art.Smooth(g);
            float line = Math.Max(1f, Theme.Scale) * (selected ? 2 : 1);
            var r = new RectangleF(line / 2, line / 2, Width - line - 1, Height - line - 1);
            using (GraphicsPath p = Art.RoundRect(r, Theme.S(10)))
            {
                Color fill = selected ? Theme.Selected : marker && Hover && !overRemove ? Theme.CardHover : Theme.CardBg;
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(selected ? Theme.Accent : Theme.CardBorder, line)) g.DrawPath(pen, p);
            }

            int left = Theme.S(26);
            if (marker)
            {
                float dot = Theme.S(22), dotX = Theme.S(26), dotY = (Height - dot) / 2f;
                using (var pen = new Pen(selected ? Theme.Accent : Theme.SlateLight, Theme.S(2)))
                    g.DrawEllipse(pen, dotX, dotY, dot, dot);
                if (selected)
                {
                    float inner = dot - Theme.S(10);
                    using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, dotX + Theme.S(5), dotY + Theme.S(5), inner, inner);
                }
                left = Theme.S(70);
            }

            int right = Width - Theme.S(26);
            if (removable)
            {
                Rectangle remove = RemoveBox;
                if (overRemove)
                {
                    using (GraphicsPath p = Art.RoundRect(remove, Theme.S(10)))
                    using (var b = new SolidBrush(Color.FromArgb(60, Theme.Red))) g.FillPath(b, p);
                }
                TextRenderer.DrawText(g, Glyph.Delete, Fonts.OutlineIcon, remove, overRemove ? Theme.Red : Theme.SlateLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                right = remove.Left - Theme.S(16);
            }

            int width = Math.Max(10, right - left);
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            int titleH = TextRenderer.MeasureText(g, "Ag", Fonts.CardTitle, new Size(width, 1000), TextFormatFlags.NoPadding).Height;
            int bodyH = TextRenderer.MeasureText(g, "Ag", Fonts.CardText, new Size(width, 1000), TextFormatFlags.NoPadding).Height;
            int y = (Height - titleH - Theme.S(4) - bodyH) / 2;
            TextRenderer.DrawText(g, Text, Fonts.CardTitle, new Rectangle(left, y, width, titleH), Theme.Text, flags);
            TextRenderer.DrawText(g, body, Fonts.CardText, new Rectangle(left, y + titleH + Theme.S(4), width, bodyH), Theme.Muted, flags);
        }
    }

    public enum HeroMode { Off, Busy, On, Error }

    // The status picture, kept minimal: a soft blob behind the shield, a big title, a subtitle, and a small badge
    // under them (for example the live ping). The blob turns while busy, and slowly while on; it rests while the
    // window is minimized, hidden next to the clock, or showing another page.
    public class HeroView : PaintedControl
    {
        readonly Timer spinner = new Timer { Interval = 30 };
        HeroMode mode;
        string title = "", subtitle = "";
        bool subtitleIsError, degraded;
        float spin;
        Color badgeDot;
        string badgeText;
        Form hooked;

        public HeroView()
        {
            BackColor = Theme.ContentBg;
            spinner.Tick += (s, e) => { spin = (spin + (mode == HeroMode.Busy ? 1.2f : 0.35f)) % 360; Invalidate(); };
        }

        public void Set(HeroMode mode, string title, string subtitle, bool subtitleIsError)
        {
            this.mode = mode;
            this.title = title ?? "";
            this.subtitle = subtitle ?? "";
            this.subtitleIsError = subtitleIsError;
            UpdateSpinner();
            Invalidate();
        }

        void UpdateSpinner()
        {
            Form form = FindForm();
            if (form != hooked)
            {
                if (hooked != null) { hooked.Resize -= OnFormChanged; hooked.VisibleChanged -= OnFormChanged; }
                hooked = form;
                if (hooked != null) { hooked.Resize += OnFormChanged; hooked.VisibleChanged += OnFormChanged; }
            }
            bool onScreen = Visible && form != null && form.Visible && form.WindowState != FormWindowState.Minimized;
            spinner.Enabled = onScreen && (mode == HeroMode.Busy || mode == HeroMode.On);
        }

        void OnFormChanged(object sender, EventArgs e) { UpdateSpinner(); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateSpinner();
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            UpdateSpinner();
        }

        // On, but the connection is poor: the shield and blob turn orange.
        public bool Degraded
        {
            get { return degraded; }
            set { if (degraded == value) return; degraded = value; Invalidate(); }
        }

        // A small pill under the subtitle with a colored dot; text null hides it.
        public void SetBadge(Color dot, string text)
        {
            if (badgeDot == dot && badgeText == text) return;
            badgeDot = dot;
            badgeText = text;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                spinner.Dispose();
                if (hooked != null) { hooked.Resize -= OnFormChanged; hooked.VisibleChanged -= OnFormChanged; hooked = null; }
            }
            base.Dispose(disposing);
        }

        // A rounded, slightly uneven shape (like a pebble), turned by angle degrees.
        static GraphicsPath Blob(float cx, float cy, float radius, float angle)
        {
            const int points = 18;
            var pts = new PointF[points];
            double turn = angle * Math.PI / 180;
            for (int i = 0; i < points; i++)
            {
                double a = i * 2 * Math.PI / points;
                double r = radius * (1 + 0.07 * Math.Sin(3 * a + 0.6) + 0.05 * Math.Sin(2 * a + 2.1) + 0.03 * Math.Sin(5 * a));
                pts[i] = new PointF(cx + (float)(r * Math.Cos(a + turn)), cy + (float)(r * Math.Sin(a + turn)));
            }
            var p = new GraphicsPath();
            p.AddClosedCurve(pts, 0.55f);
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Width < 50 || Height < 50) return;
            Art.Smooth(g);

            int badgeH = Theme.S(30), subtitleH = Theme.S(44), titleH = Theme.S(48);
            int badgeTop = Height - Theme.S(4) - badgeH;
            int subtitleTop = badgeTop - Theme.S(4) - subtitleH;
            int titleTop = subtitleTop - titleH;
            int areaTop = Theme.S(28), areaBottom = titleTop - Theme.S(6);

            // The halo reaches ~1.16 x radius plus its wobble, so size the blob for the halo to fit the area
            // and keep a clear gap above it instead of touching the top edge.
            float cx = Width / 2f, cy = (areaTop + areaBottom) / 2f;
            float radius = Math.Max(Theme.S(30), Math.Min(Theme.S(118), (areaBottom - areaTop) / 2f / 1.3f));
            Color tint, shieldLight = Theme.AccentLight, shieldDark = Theme.AccentDark;
            switch (mode)
            {
                case HeroMode.On:
                    if (degraded) { tint = Theme.Orange; shieldLight = Color.FromArgb(255, 170, 80); shieldDark = Theme.OrangeDark; }
                    else { tint = Theme.Green; shieldLight = Theme.GreenLight; shieldDark = Theme.Green; }
                    break;
                case HeroMode.Busy:
                    tint = Theme.Yellow;
                    break;
                case HeroMode.Error:
                    tint = Theme.Red; shieldLight = Color.FromArgb(255, 110, 110); shieldDark = Theme.RedDark;
                    break;
                default:
                    tint = Theme.Accent;
                    break;
            }

            // A faint wide halo, then the blob itself.
            using (GraphicsPath halo = Blob(cx, cy, radius * 1.16f, spin + 25))
            using (var b = new SolidBrush(Color.FromArgb(mode == HeroMode.Busy ? 22 : 28, tint)))
                g.FillPath(b, halo);
            using (GraphicsPath blob = Blob(cx, cy, radius, spin))
            using (var b = new PathGradientBrush(blob))
            {
                b.CenterPoint = new PointF(cx - radius * 0.2f, cy - radius * 0.25f);
                b.CenterColor = Color.FromArgb(mode == HeroMode.Busy ? 70 : 90, tint);
                b.SurroundColors = new[] { Color.FromArgb(mode == HeroMode.Busy ? 40 : 52, tint) };
                g.FillPath(b, blob);
            }

            float shieldH = radius * 1.02f, shieldW = shieldH * 0.86f;
            Art.DrawShield(g, new RectangleF(cx - shieldW / 2, cy - shieldH / 2, shieldW, shieldH), shieldLight, shieldDark);

            TextRenderer.DrawText(g, title, Fonts.HeroTitle, new Rectangle(0, titleTop, Width, titleH), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, subtitle, Fonts.HeroText, new Rectangle(Theme.S(30), subtitleTop, Width - Theme.S(60), subtitleH),
                subtitleIsError ? Theme.Red : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

            if (!string.IsNullOrEmpty(badgeText))
            {
                int textW = TextRenderer.MeasureText(g, badgeText, Fonts.Badge, new Size(2000, 100), TextFormatFlags.NoPadding).Width;
                int dot = Theme.S(9), pad = Theme.S(12), w = Math.Min(Width - Theme.S(20), pad + dot + Theme.S(8) + textW + pad);
                var pill = new RectangleF((Width - w) / 2f, badgeTop, w, badgeH);
                using (GraphicsPath p = Art.RoundRect(pill, badgeH / 2f))
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, badgeDot))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(90, badgeDot), Math.Max(1f, Theme.Scale))) g.DrawPath(pen, p);
                }
                using (var b = new SolidBrush(badgeDot)) g.FillEllipse(b, pill.X + pad, pill.Y + (badgeH - dot) / 2f, dot, dot);
                int tx = (int)(pill.X + pad + dot + Theme.S(8));
                TextRenderer.DrawText(g, badgeText, Fonts.Badge, new Rectangle(tx, badgeTop, (int)pill.Right - tx - pad + Theme.S(4), badgeH), Theme.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // A small card on the home page with the last few lines of the log, newest at the bottom. Clicking it
    // opens the full log (OpenLog). Feed it with SetLines, or let it follow a LogPage.
    public class MiniLog : Card
    {
        const int Shown = 4;
        string[] lines = new string[0];
        public Action OpenLog;

        public MiniLog()
        {
            Cursor = Cursors.Hand;
            BackColor = Theme.Rail;
        }

        // Mirrors the end of a LogPage's text box (Nevatch Server writes its log straight into one).
        public static MiniLog Following(LogPage page)
        {
            var mini = new MiniLog();
            page.Box.TextChanged += (s, e) => mini.SetLines(Tail(page.Box.Text, Shown));
            return mini;
        }

        static string[] Tail(string text, int count)
        {
            var found = new List<string>();
            int end = text.Length;
            while (end > 0 && found.Count < count)
            {
                while (end > 0 && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
                if (end == 0) break;
                int start = text.LastIndexOf('\n', end - 1) + 1;
                found.Insert(0, text.Substring(start, end - start).TrimEnd('\r'));
                end = start;
            }
            return found.ToArray();
        }

        public void SetLines(string[] all)
        {
            int n = Math.Min(Shown, all.Length);
            var last = new string[n];
            Array.Copy(all, all.Length - n, last, 0, n);
            if (last.Length == lines.Length)
            {
                bool same = true;
                for (int i = 0; i < n && same; i++) same = last[i] == lines[i];
                if (same) return;
            }
            lines = last;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            if (OpenLog != null) OpenLog();
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int left = Theme.S(16), width = Width - left * 2;
            TextRenderer.DrawText(g, "ACTIVITY", Fonts.MiniLogTitle, new Rectangle(left, Theme.S(9), width, Theme.S(16)), Theme.NavText,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, "Full log  ›", Fonts.MiniLogTitle, new Rectangle(left, Theme.S(9), width, Theme.S(16)), Theme.NavText,
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.Right);
            int lineH = TextRenderer.MeasureText(g, "Ag", Fonts.MiniLog, new Size(1000, 100), TextFormatFlags.NoPadding).Height + Theme.S(2);
            int y = Theme.S(30);
            if (lines.Length == 0)
            {
                TextRenderer.DrawText(g, "Nothing yet.", Fonts.MiniLog, new Rectangle(left, y, width, lineH), Theme.NavText,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                return;
            }
            for (int i = 0; i < lines.Length && y + lineH <= Height - Theme.S(6); i++, y += lineH)
                TextRenderer.DrawText(g, lines[i], Fonts.MiniLog, new Rectangle(left, y, width, lineH),
                    i == lines.Length - 1 ? Theme.Muted : Theme.NavText,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // ---------- pages ----------

    // The home page: status picture, the big button under it, a card, and optionally the activity log at the bottom.
    public class HomePage : Panel
    {
        readonly HeroView hero;
        readonly PillButton button;
        readonly InfoCard card;
        readonly MiniLog log;

        public HomePage(HeroView hero, PillButton button, InfoCard card) : this(hero, button, card, null) { }

        public HomePage(HeroView hero, PillButton button, InfoCard card, MiniLog log)
        {
            this.hero = hero;
            this.button = button;
            this.card = card;
            this.log = log;
            BackColor = Theme.ContentBg;
            DoubleBuffered = true;
            Controls.Add(hero);
            Controls.Add(button);
            Controls.Add(card);
            if (log != null) Controls.Add(log);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w = ClientSize.Width, h = ClientSize.Height;
            int contentW = Math.Min(w - Theme.S(56), Theme.S(620)), x = (w - contentW) / 2;
            int bottom = h - Theme.S(24);
            if (log != null)
            {
                int logH = Theme.S(100);
                log.SetBounds(x, bottom - logH, contentW, logH);
                bottom -= logH + Theme.S(12);
            }
            int cardH = Theme.S(84), cardY = bottom - cardH;
            int buttonW = Math.Min(contentW, Theme.S(380)), buttonH = Theme.S(70), buttonY = cardY - buttonH - Theme.S(12);
            hero.SetBounds(0, 0, w, Math.Max(0, buttonY - Theme.S(4)));
            button.SetBounds((w - buttonW) / 2, buttonY, buttonW, buttonH);
            card.SetBounds(x, cardY, contentW, cardH);
        }
    }

    // A page with a heading and rows stacked under it; a row with height -1 fills the rest.
    public class Page : Panel
    {
        readonly string heading;
        readonly List<Control> rows = new List<Control>();
        readonly List<int> heights = new List<int>();
        readonly List<int> widths = new List<int>();

        public Page(string heading)
        {
            this.heading = heading;
            BackColor = Theme.ContentBg;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        public T Add<T>(T control, int height) where T : Control { return Add(control, height, 0); }

        public T Add<T>(T control, int height, int width) where T : Control
        {
            rows.Add(control);
            heights.Add(height);
            widths.Add(width);
            Controls.Add(control);
            PerformLayout();
            return control;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Theme.S(36), full = ClientSize.Width - Theme.S(72), y = Theme.S(96);
            for (int i = 0; i < rows.Count; i++)
            {
                int h = heights[i] < 0 ? Math.Max(Theme.S(60), ClientSize.Height - y - Theme.S(30)) : Theme.S(heights[i]);
                int w = widths[i] > 0 ? Theme.S(widths[i]) : full;
                rows[i].SetBounds(x, y, w, h);
                y += h + Theme.S(16);
            }
        }

        public string HeadingGlyph; // set by ShellForm.AddPage; shown before the heading like a channel's icon

        // A Discord channel header: icon, name, and a shadow line under it.
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            int barH = Theme.S(64), x = Theme.S(28);
            if (!string.IsNullOrEmpty(HeadingGlyph))
            {
                TextRenderer.DrawText(g, HeadingGlyph, Fonts.PageTitleIcon, new Rectangle(x, 0, Theme.S(26), barH), Theme.NavText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += Theme.S(36);
            }
            TextRenderer.DrawText(g, heading, Fonts.PageTitle, new Rectangle(x, 0, ClientSize.Width - x - Theme.S(20), barH), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            using (var b = new SolidBrush(Theme.CardBorder)) g.FillRectangle(b, 0, barH - Theme.S(1), ClientSize.Width, Math.Max(1, Theme.S(1)));
            using (var b = new SolidBrush(Color.FromArgb(18, 0, 0, 0))) g.FillRectangle(b, 0, barH, ClientSize.Width, Theme.S(2));
        }
    }

    // ---------- slim scroll bars ----------
    // Windows' own scroll bars look out of place even in their dark style, so everything that scrolls shows this
    // thin rounded thumb instead: our own stacks scroll themselves (ScrollPanel), and a TextBox or ListView sits in
    // a NativeScrollHost that hides its real scroll bar under the slim one.

    public interface IScrollSource
    {
        int ScrollValue { get; }   // first visible unit
        int ScrollMax { get; }     // all units
        int ScrollPage { get; }    // units in view
        void ScrollToValue(int value);
    }

    public class SlimScrollBar : Control
    {
        readonly IScrollSource source;
        bool hover, dragging;
        int dragOffset, lastValue = -1, lastMax = -1, lastPage = -1;

        public SlimScrollBar(IScrollSource source)
        {
            this.source = source;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        public static int BarWidth { get { return Theme.S(12); } }

        public bool Needed { get { return source.ScrollPage > 0 && source.ScrollMax > source.ScrollPage; } }

        int Inset { get { return Theme.S(4); } }

        Rectangle Thumb(out int track, out int thumbH)
        {
            int max = source.ScrollMax, page = source.ScrollPage;
            track = Height - Inset * 2;
            thumbH = 0;
            if (!Needed || track <= 0) return Rectangle.Empty;
            thumbH = Math.Min(track, Math.Max(Theme.S(32), (int)((long)track * page / max)));
            int range = max - page;
            int y = Inset + (int)((long)(track - thumbH) * Math.Max(0, Math.Min(source.ScrollValue, range)) / range);
            int w = hover || dragging ? Theme.S(8) : Theme.S(5);
            return new Rectangle((Width - w) / 2, y, w, thumbH);
        }

        // Repaints when the source moved; cheap enough to call on every tick.
        public void Sync()
        {
            int v = source.ScrollValue, m = source.ScrollMax, p = source.ScrollPage;
            if (v == lastValue && m == lastMax && p == lastPage) return;
            lastValue = v; lastMax = m; lastPage = p;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int track, thumbH;
            Rectangle r = Thumb(out track, out thumbH);
            if (r.IsEmpty) return;
            Art.Smooth(g);
            Color c = dragging ? Theme.Muted : hover ? Theme.NavText : Theme.SlateLight;
            using (GraphicsPath p = Art.RoundRect(r, r.Width / 2f))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int track, thumbH;
            Rectangle r = Thumb(out track, out thumbH);
            if (r.IsEmpty) return;
            if (e.Y >= r.Top && e.Y < r.Bottom)
            {
                dragging = true;
                dragOffset = e.Y - r.Top;
                Capture = true;
                Invalidate();
            }
            else source.ScrollToValue(source.ScrollValue + (e.Y < r.Top ? -1 : 1) * Math.Max(1, source.ScrollPage - 1));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!hover) { hover = true; Invalidate(); }
            if (!dragging) return;
            int track, thumbH;
            Thumb(out track, out thumbH);
            if (track - thumbH <= 0) return;
            int range = source.ScrollMax - source.ScrollPage;
            source.ScrollToValue((int)((long)(e.Y - dragOffset - Inset) * range / (track - thumbH)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover) { hover = false; Invalidate(); }
        }
    }

    // A panel that scrolls its own children: subclasses call SetContentHeight first in OnLayout, then place the
    // children ScrollOffset higher, and keep SlimScrollBar.BarWidth free on the right.
    public class ScrollPanel : Panel, IScrollSource
    {
        protected readonly SlimScrollBar Bar;
        int offset, content;

        public ScrollPanel()
        {
            BackColor = Theme.ContentBg;
            DoubleBuffered = true;
            Bar = new SlimScrollBar(this) { BackColor = Theme.ContentBg };
            Controls.Add(Bar);
        }

        protected int ScrollOffset { get { return offset; } }

        protected void SetContentHeight(int height)
        {
            content = height;
            offset = Math.Max(0, Math.Min(offset, content - ClientSize.Height));
            int w = SlimScrollBar.BarWidth;
            Bar.SetBounds(ClientSize.Width - w, 0, w, ClientSize.Height);
            Bar.Visible = Bar.Needed;
            Bar.BringToFront();
            Bar.Sync();
        }

        public int ScrollValue { get { return offset; } }
        public int ScrollMax { get { return content; } }
        public int ScrollPage { get { return ClientSize.Height; } }

        public void ScrollToValue(int value)
        {
            value = Math.Max(0, Math.Min(value, content - ClientSize.Height));
            if (value == offset) return;
            offset = value;
            PerformLayout();
            Update();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
            ScrollToValue(offset - e.Delta * lines * Theme.S(20) / 120);
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
    }

    // Hosts a TextBox or ListView: the control is made wider by its own scroll bar, which then sits under the slim one.
    public class NativeScrollHost : Panel, IScrollSource
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ScrollInfo { public int Size, Mask, Min, Max, Page, Pos, TrackPos; }
        [DllImport("user32.dll")]
        static extern bool GetScrollInfo(IntPtr hwnd, int bar, ref ScrollInfo info);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        const int SB_VERT = 1, SIF_ALL = 0x17, EM_LINESCROLL = 0xB6, LVM_SCROLL = 0x1014;

        public readonly Control Inner;
        readonly SlimScrollBar bar;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 60 };

        public NativeScrollHost(Control inner)
        {
            Inner = inner;
            BackColor = inner.BackColor;
            bar = new SlimScrollBar(this) { BackColor = inner.BackColor };
            Controls.Add(inner);
            Controls.Add(bar);
            bar.BringToFront();
            // Neither control tells when it scrolled, so look a few times a second while it's on screen.
            timer.Tick += (s, e) => Sync();
            VisibleChanged += (s, e) => timer.Enabled = Visible;
            timer.Enabled = Visible;
            Disposed += (s, e) => timer.Dispose();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int w = SlimScrollBar.BarWidth;
            Inner.SetBounds(0, 0, Math.Max(0, ClientSize.Width - w + SystemInformation.VerticalScrollBarWidth), ClientSize.Height);
            bar.SetBounds(ClientSize.Width - w, 0, w, ClientSize.Height);
            bar.BringToFront();
            Sync();
        }

        ScrollInfo Info()
        {
            var si = new ScrollInfo { Size = Marshal.SizeOf(typeof(ScrollInfo)), Mask = SIF_ALL };
            if (!Inner.IsHandleCreated || !GetScrollInfo(Inner.Handle, SB_VERT, ref si)) return new ScrollInfo();
            return si;
        }

        public int ScrollValue { get { ScrollInfo si = Info(); return si.Pos - si.Min; } }
        public int ScrollMax { get { ScrollInfo si = Info(); return si.Page == 0 ? 0 : si.Max - si.Min + 1; } }
        public int ScrollPage { get { return Info().Page; } }

        public void ScrollToValue(int value)
        {
            int delta = value - ScrollValue;
            if (delta == 0 || !Inner.IsHandleCreated) return;
            if (Inner is TextBoxBase) SendMessage(Inner.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            else
            {
                var list = Inner as ListView;
                if (list != null && list.Items.Count > 0)
                    SendMessage(Inner.Handle, LVM_SCROLL, IntPtr.Zero, (IntPtr)(delta * list.GetItemRect(0).Height));
            }
            Sync();
        }

        // The bar always stays: it also covers the control's real scroll bar, and draws nothing when not needed.
        void Sync() { bar.Sync(); }

        // Wheel over the slim bar: scroll the control.
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollToValue(ScrollValue - e.Delta * Math.Max(1, SystemInformation.MouseWheelScrollLines) / 120);
        }
    }

    // A scrolling stack of same-height rows, with a card that shows when there are none.
    public class RowList : ScrollPanel
    {
        readonly List<Control> rows = new List<Control>();
        readonly InfoCard empty;
        readonly int rowHeight;

        public RowList(int rowHeight, string glyph, string emptyTitle, string emptyText)
        {
            this.rowHeight = rowHeight;
            empty = new InfoCard(glyph, null);
            empty.Set(emptyTitle, emptyText);
            Controls.Add(empty);
        }

        public void SetEmpty(string title, string text) { empty.Set(title, text); }

        public void SetRows(IEnumerable<Control> newRows)
        {
            SuspendLayout();
            foreach (Control row in rows)
            {
                Controls.Remove(row);
                row.Dispose();
            }
            rows.Clear();
            rows.AddRange(newRows);
            foreach (Control row in rows) Controls.Add(row);
            empty.Visible = rows.Count == 0;
            ResumeLayout(true);
        }

        // Rows first, then the scroll area: the other way round, rows still at the old width bring up a sideways scroll bar.
        protected override void OnLayout(LayoutEventArgs e)
        {
            // ScrollPanel's constructor adds its bar, which lays out before this constructor has made the empty card.
            if (empty == null) { base.OnLayout(e); return; }
            int gap = Theme.S(12), total = 0;
            var heights = new int[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                var sized = rows[i] as ISizedRow;
                heights[i] = Theme.S(sized != null ? sized.RowHeight : rowHeight);
                total += heights[i] + gap;
            }
            SetContentHeight(total);
            int width = ClientSize.Width - (total > ClientSize.Height ? SlimScrollBar.BarWidth : 0);
            empty.SetBounds(0, 0, width, Theme.S(100));
            for (int i = 0, y = 0; i < rows.Count; y += heights[i] + gap, i++)
                rows[i].SetBounds(0, y - ScrollOffset, width, heights[i]);
            base.OnLayout(e);
        }
    }

    // A RowList row that sets its own height (unscaled), for rows that open up; it calls the list's PerformLayout when it changes.
    public interface ISizedRow
    {
        int RowHeight { get; }
    }

    // Outline buttons side by side, left to right.
    public class ButtonRow : Panel
    {
        readonly OutlineButton[] buttons;

        public ButtonRow(params OutlineButton[] buttons)
        {
            this.buttons = buttons;
            BackColor = Theme.ContentBg;
            Controls.AddRange(buttons);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = 0;
            foreach (OutlineButton b in buttons)
            {
                b.SetBounds(x, 0, Theme.S(150), ClientSize.Height);
                x += Theme.S(162);
            }
        }
    }

    public class LogPage : Page
    {
        public readonly TextBox Box = new TextBox();

        public LogPage() : base("Log")
        {
            var copy = new OutlineButton { Text = "Copy log" };
            copy.Click += (s, e) =>
            {
                if (Box.TextLength == 0) return;
                try { Clipboard.SetText(Box.Text); copy.Text = "Copied"; } catch { }
            };
            Box.Multiline = true;
            Box.ReadOnly = true;
            Box.ScrollBars = ScrollBars.Vertical;
            Box.WordWrap = true;
            Box.Font = Fonts.Mono;
            Box.BackColor = Theme.InputBg;
            Box.ForeColor = Theme.Muted;
            Box.BorderStyle = BorderStyle.None;
            Dark.Controls(Box);
            Add(copy, 44, 150);
            Add(new NativeScrollHost(Box), -1);
            VisibleChanged += (s, e) => copy.Text = "Copy log";
        }

        public void SetLines(string[] lines)
        {
            string text = string.Join("\r\n", lines);
            if (text == Box.Text) return;
            Box.Text = text;
            Box.SelectionStart = Box.TextLength;
            Box.ScrollToCaret();
        }

        public void Append(string line)
        {
            if (Box.TextLength > 300000) Box.Text = Box.Text.Substring(150000);
            Box.AppendText(line + "\r\n");
        }
    }

    // Two lines of round-trip times in ms, newest on the right; a value below 0 means no answer (red mark on top).
    public class LagGraph : Card
    {
        static readonly int[] Scales = { 100, 200, 300, 500, 1000, 1500, 2000, 3000 };
        readonly int slots;
        int[] main = new int[0], other = new int[0];
        public string MainName = "", OtherName = "";

        public LagGraph(int slots) { this.slots = slots; }

        public void SetData(int[] mainMs, int[] otherMs)
        {
            main = mainMs;
            other = otherMs;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Art.Smooth(g);
            int pad = Theme.S(22), labelW = Theme.S(64), legendH = Theme.S(30), footH = Theme.S(22);
            var plot = new Rectangle(pad + labelW, pad + legendH, Width - pad * 2 - labelW, Height - pad * 2 - legendH - footH);
            if (plot.Width < 20 || plot.Height < 20) return;

            int top = 0;
            foreach (int[] series in new[] { main, other })
                foreach (int v in series) top = Math.Max(top, v);
            int scale = Scales[Scales.Length - 1];
            foreach (int s in Scales) if (top <= s) { scale = s; break; }

            const TextFormatFlags right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            using (var grid = new Pen(Theme.Border, Math.Max(1f, Theme.Scale)))
            {
                for (int i = 0; i <= 2; i++)
                {
                    int y = plot.Bottom - plot.Height * i / 2;
                    g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, scale * i / 2 + " ms", Fonts.CardText,
                        new Rectangle(pad, y - Theme.S(10), labelW - Theme.S(10), Theme.S(20)), Theme.Muted, right);
                }
            }
            TextRenderer.DrawText(g, "2 minutes ago", Fonts.CardText, new Point(plot.Left, plot.Bottom + Theme.S(6)), Theme.Muted,
                TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "now", Fonts.CardText, new Rectangle(plot.Left, plot.Bottom + Theme.S(6), plot.Width, Theme.S(20)),
                Theme.Muted, TextFormatFlags.Right | TextFormatFlags.NoPadding);

            Color otherColor = Theme.SlateLight, lostColor = Theme.Red;
            Legend(g, pad + labelW, pad, Theme.Accent, MainName);
            Legend(g, pad + labelW + Theme.S(210), pad, otherColor, OtherName);

            DrawLine(g, plot, other, scale, otherColor, 1.5f, false);
            DrawLine(g, plot, main, scale, Theme.Accent, 2.5f, true);
        }

        void Legend(Graphics g, int x, int y, Color color, string text)
        {
            using (var b = new SolidBrush(color)) g.FillEllipse(b, x, y + Theme.S(5), Theme.S(12), Theme.S(12));
            TextRenderer.DrawText(g, text, Fonts.CardText, new Point(x + Theme.S(18), y + Theme.S(2)), Theme.Text, TextFormatFlags.NoPadding);
        }

        void DrawLine(Graphics g, Rectangle plot, int[] values, int scale, Color color, float width, bool markLost)
        {
            float step = plot.Width / (float)Math.Max(1, slots - 1);
            float x0 = plot.Right - (values.Length - 1) * step;
            var points = new List<PointF>();
            using (var pen = new Pen(color, Theme.S(1) * width) { LineJoin = LineJoin.Round })
            using (var lost = new Pen(Theme.Red, Theme.S(3)))
            {
                for (int i = 0; i <= values.Length; i++)
                {
                    // A gap (no answer) or the end draws the stretch collected so far.
                    if (i == values.Length || values[i] < 0)
                    {
                        if (points.Count > 1) g.DrawLines(pen, points.ToArray());
                        else if (points.Count == 1) using (var b = new SolidBrush(color)) g.FillEllipse(b, points[0].X - 2, points[0].Y - 2, 4, 4);
                        points.Clear();
                        if (i < values.Length && markLost)
                        {
                            float x = x0 + i * step;
                            g.DrawLine(lost, x, plot.Top, x, plot.Top + Theme.S(14));
                        }
                        continue;
                    }
                    float y = plot.Bottom - plot.Height * Math.Min(values[i], scale) / (float)scale;
                    points.Add(new PointF(x0 + i * step, y));
                }
            }
        }
    }

    public class AboutPage : Page
    {
        public AboutPage(string product, string text) : base("About")
        {
            var card = new InfoCard(Glyph.Info, "Xray source code");
            Version v = System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
            card.Set(product + " " + v.Major + "." + v.Minor + "." + v.Build,
                text + "\n\nIncludes Xray-core, licensed under the Mozilla Public License 2.0. Its source code is at github.com/XTLS/Xray-core.");
            card.Button.Click += (s, e) => { try { Process.Start("https://github.com/XTLS/Xray-core"); } catch { } };
            Add(card, 170);
        }
    }
}
