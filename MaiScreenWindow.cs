using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenLens
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Config cfg = Config.Load();
            AutoStart.Apply(cfg.AutoStart);
            Application.Run(new LensForm(cfg, Array.IndexOf(args, "--pin") >= 0));
        }
    }

    internal static class Native
    {
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        public const int VK_MENU = 0x12;
        public const int VK_O = 0x4F;


        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_PIN = 0x5C01;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT pt);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
        
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
        
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

    }
    
    // config.ini
    internal sealed class Config
    {
        public const string FileName = "config.ini";

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = 360;
        public int Height { get; set; } = 280;
        public double Zoom { get; set; } = 1.0;
        
        public bool AutoStart { get; set; } = false;

        public string Path { get; private set; } = "";

        public static Config Load()
        {
            var cfg = new Config { Path = ResolvePath() };
            
            Rectangle wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            cfg.X = wa.Right - cfg.Width - 32;
            cfg.Y = wa.Bottom - cfg.Height - 32;

            if (File.Exists(cfg.Path))
            {
                try
                {
                    foreach (string raw in File.ReadAllLines(cfg.Path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;

                        string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string val = line.Substring(eq + 1).Trim();
                        switch (key)
                        {
                            case "x": cfg.X = ParseInt(val, cfg.X); break;
                            case "y": cfg.Y = ParseInt(val, cfg.Y); break;
                            case "width": case "w": cfg.Width = ParseInt(val, cfg.Width); break;
                            case "height": case "h": cfg.Height = ParseInt(val, cfg.Height); break;
                            case "zoom": cfg.Zoom = ParseDouble(val, cfg.Zoom); break;
                            case "autostart": case "startup": case "runatstartup":
                                cfg.AutoStart = ParseBool(val, cfg.AutoStart);
                                break;
                        }
                    }
                }
                catch { }
            }

            cfg.Width = Math.Clamp(cfg.Width, 80, 4000);
            cfg.Height = Math.Clamp(cfg.Height, 60, 4000);
            cfg.Zoom = Math.Clamp(cfg.Zoom, 0.25, 12.0);
            
            Rectangle vs = SystemInformation.VirtualScreen;
            cfg.X = Math.Clamp(cfg.X, vs.Left - cfg.Width + 120, vs.Right - 120);
            cfg.Y = Math.Clamp(cfg.Y, vs.Top, vs.Bottom - 60);

            if (!File.Exists(cfg.Path)) cfg.Save();
            return cfg;
        }
        
        private static string ResolvePath()
        {
            string exeDir = System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            DirectoryInfo? dir = new DirectoryInfo(exeDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate)) return candidate;
                try
                {
                    if (dir.GetFiles("*.csproj").Length > 0) return candidate;
                }
                catch { }
                dir = dir.Parent;
            }
            return System.IO.Path.Combine(exeDir, FileName);
        }

        private static int ParseInt(string s, int fallback)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            switch (s.Trim())
            {
                case "1": return true;
                case "0": return false;
                default: return fallback;
            }
        }

        private static double ParseDouble(string s, double fallback)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        public void Save()
        {
            try
            {
                string nl = Environment.NewLine;
                File.WriteAllText(Path,
                    "; 更改之后托盘右键 -> 重新加载配置（或重启程序）" + nl +
                    nl +
                    "; 小窗左上角坐标" + nl +
                    "x=" + X + nl +
                    "y=" + Y + nl +
                    nl +
                    "; 小窗大小" + nl +
                    "width=" + Width + nl +
                    "height=" + Height + nl +
                    nl +
                    "; 放大倍数" + nl +
                    "zoom=" + Zoom.ToString("0.##", CultureInfo.InvariantCulture) + nl +
                    nl +
                    "; 开机自启：1 打开 / 0 关闭（默认关闭）" + nl +
                    "autostart=" + (AutoStart ? "1" : "0") + nl);
            }
            catch { }
        }
    }
    
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MaiScreenWindow";
        private static string ExePath() =>
            Environment.ProcessPath ?? Application.ExecutablePath;

        public static void Apply(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;

                if (enable)
                {
                    string cmd = "\"" + ExePath() + "\"";
                    if (!string.Equals(key.GetValue(ValueName) as string, cmd, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, cmd, RegistryValueKind.String);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch { }
        }

    }

    internal sealed class LensForm : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020; 
        private const int WS_EX_TOOLWINDOW = 0x00000080;  
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000; 

        private Config _cfg;
        private readonly System.Windows.Forms.Timer _timer =
            new System.Windows.Forms.Timer { Interval = 30 };
        private readonly Font _font = new Font("Consolas", 9f, FontStyle.Bold);

        private Bitmap? _shot;
        private Rectangle _src = Rectangle.Empty;
        private Point _cursor;
        private bool _pinned;
        private bool _active;

        private IntPtr _hook = IntPtr.Zero;
        private Native.LowLevelKeyboardProc? _hookProc;
        private volatile bool _altHeld;
        private string? _errText;
        private NotifyIcon? _tray;


        public LensForm(Config cfg, bool startPinned = false)
        {
            _cfg = cfg;
            _pinned = startPinned;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            ApplyConfig(cfg);

            Opacity = 0;

            _timer.Tick += (_, _) => Tick();
        }

        private void ApplyConfig(Config cfg)
        {
            _cfg = cfg;
            ClientSize = new Size(cfg.Width, cfg.Height);
            Location = new Point(cfg.X, cfg.Y);
            Invalidate();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "ScreenLens：按住 Alt 放大鼠标附近，Ctrl+Alt+O 固定",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("固定 / 取消固定  (Ctrl+Alt+O)", null, (_, _) => TogglePin());
            menu.Items.Add("重新加载 config.ini", null, (_, _) => ReloadConfig());
            menu.Items.Add("打开 config.ini", null, (_, _) => OpenConfig());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => Close());
            _tray.ContextMenuStrip = menu;
            
            if (!Native.RegisterHotKey(Handle, Native.HOTKEY_PIN,
                    Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_O))
            {
                _tray.ShowBalloonTip(5000, "热键注册失败",
                    "Ctrl+Alt+O 被其他程序占用。仍可用托盘菜单里的「固定 / 取消固定」。",
                    ToolTipIcon.Warning);
            }

            _hookProc = HookProc;
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _hookProc,
                Native.GetModuleHandle(null), 0);

            _timer.Start();
        }

        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN ||
                    msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP)
                {
                    Native.KBDLLHOOKSTRUCT info =
                        Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
                    if (info.vkCode == Native.VK_MENU ||
                        info.vkCode == Native.VK_LMENU ||
                        info.vkCode == Native.VK_RMENU)
                    {
                        _altHeld = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                    }
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == Native.HOTKEY_PIN)
            {
                TogglePin();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        }

        private void ReloadConfig()
        {
            Config cfg = Config.Load();
            ApplyConfig(cfg);
            AutoStart.Apply(cfg.AutoStart);
            _shot?.Dispose();
            _shot = null;
            Tick();
        }

        private void OpenConfig()
        {
            try
            {
                if (!File.Exists(_cfg.Path)) _cfg.Save();
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_cfg.Path) { UseShellExecute = true });
            }
            catch { }
        }

        private void TogglePin()
        {
            _pinned = !_pinned;
            Tick();
        }

        private void Tick()
        {
            try
            {
                TickCore();
            }
            catch (Exception ex)
            {
                if (_errText != ex.Message)
                {
                    _errText = ex.Message;
                    Invalidate();
                }
            }
        }

        private void TickCore()
        {
            Native.GetCursorPos(out Native.POINT p);
            _cursor = new Point(p.X, p.Y);

            bool fromHook = _altHeld;
            bool fromPoll = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0;
            bool altDown = fromHook || fromPoll;

            if (!_pinned && !altDown)
            {
                if (_active)
                {
                    _active = false;
                    Opacity = 0;
                }
                return;
            }

            if (!_active)
            {
                _active = true;
                Opacity = 0.97;
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
            }

            GrabScreen();
            _errText = null;
            Invalidate();
        }

        private void GrabScreen()
        {
            double zoom = _cfg.Zoom;
            int sw = Math.Max(1, (int)Math.Round(ClientSize.Width / zoom));
            int sh = Math.Max(1, (int)Math.Round(ClientSize.Height / zoom));

            Rectangle vs = SystemInformation.VirtualScreen;
            int x = Math.Clamp(_cursor.X - sw / 2, vs.Left, Math.Max(vs.Left, vs.Right - sw));
            int y = Math.Clamp(_cursor.Y - sh / 2, vs.Top, Math.Max(vs.Top, vs.Bottom - sh));
            var rect = new Rectangle(x, y, sw, sh);

            if (_shot == null || _shot.Width != sw || _shot.Height != sh)
            {
                _shot?.Dispose();
                _shot = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
            }

            using (Graphics g = Graphics.FromImage(_shot))
            {
                g.CopyFromScreen(rect.X, rect.Y, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
            }

            _src = rect;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            if (_shot != null && _src.Width > 0 && _src.Height > 0)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;
                g.DrawImage(_shot, new Rectangle(Point.Empty, ClientSize));
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_errText != null)
            {
                using var eb = new SolidBrush(Color.FromArgb(255, 255, 120, 120));
                g.DrawString("截屏失败：\n" + _errText, _font, eb,
                    new RectangleF(8, 8, ClientSize.Width - 16, ClientSize.Height - 40));
            }

            if (_src.Width > 0 && _src.Height > 0)
            {
                float sx = ClientSize.Width / (float)_src.Width;
                float sy = ClientSize.Height / (float)_src.Height;
                float cx = (_cursor.X - _src.X + 0.5f) * sx;
                float cy = (_cursor.Y - _src.Y + 0.5f) * sy;

                using var cross = new Pen(Color.FromArgb(235, 255, 48, 48), 1.5f);
                g.DrawLine(cross, cx - 16, cy, cx + 16, cy);
                g.DrawLine(cross, cx, cy - 16, cx, cy + 16);

                using var ring = new Pen(Color.FromArgb(235, 255, 230, 0), 1.2f);
                g.DrawEllipse(ring, cx - 5, cy - 5, 10, 10);
            }

            string info = _cursor.X + "," + _cursor.Y +
                          "   x" + _cfg.Zoom.ToString("0.##", CultureInfo.InvariantCulture) +
                          (_pinned ? "   PIN" : "");
            SizeF ts = g.MeasureString(info, _font);
            using (var bg = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                g.FillRectangle(bg, 3, ClientSize.Height - ts.Height - 7, ts.Width + 8, ts.Height + 3);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.DrawString(info, _font, Brushes.White, 7, ClientSize.Height - ts.Height - 5);

            using (var border = new Pen(Color.FromArgb(255, 0, 190, 255), 2f))
                g.DrawRectangle(border, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Native.UnregisterHotKey(Handle, Native.HOTKEY_PIN);
                if (_hook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }
                _timer.Dispose();
                _tray?.Dispose();
                _shot?.Dispose();
                _font.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
