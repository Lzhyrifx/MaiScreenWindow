using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

            Config config = Config.Load();
            AutoStart.Apply(config.AutoStart);
            Application.Run(new LensForm(config, Array.IndexOf(args, "--pin") >= 0));
        }
    }

    internal static class Native
    {
        public const uint WdaExcludeFromCapture = 0x00000011;

        public const int WsExTransparent = 0x00000020;
        public const int WsExToolWindow = 0x00000080;
        public const int WsExLayered = 0x00080000;
        public const int WsExNoActivate = 0x08000000;

        public const int WhKeyboardLl = 13;
        public const int WmKeyDown = 0x0100;
        public const int WmKeyUp = 0x0101;
        public const int WmSysKeyDown = 0x0104;
        public const int WmSysKeyUp = 0x0105;

        public const int WmHotkey = 0x0312;
        public const int HotkeyPin = 0x5C01;

        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;
        public const uint ModNoRepeat = 0x4000;

        public const int VkShift = 0x10;
        public const int VkControl = 0x11;
        public const int VkMenu = 0x12;
        public const int VkLWin = 0x5B;
        public const int VkRWin = 0x5C;
        public const int VkLShift = 0xA0;
        public const int VkRShift = 0xA1;
        public const int VkLControl = 0xA2;
        public const int VkRControl = 0xA3;
        public const int VkLMenu = 0xA4;
        public const int VkRMenu = 0xA5;

        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpShowWindow = 0x0040;

        public const uint MonitorDefaultToNearest = 0x00000002;

        public const int GwlStyle = -16;
        public const long WsCaption = 0x00C00000L;

        public static readonly nint HwndTopmost = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MonitorInfo
        {
            public int cbSize;
            public Rect Monitor;
            public Rect Work;
            public uint Flags;
        }

#pragma warning disable CS0649 // 字段由 Win32 钩子回调填充
        [StructLayout(LayoutKind.Sequential)]
        public struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public nint dwExtraInfo;
        }
#pragma warning restore CS0649

        public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(nint hWnd, int id);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out Point pt);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowDisplayAffinity(nint hwnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(nint hhk);

        [DllImport("user32.dll")]
        public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern nint GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(nint hWnd, StringBuilder name, int maxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(nint hWnd, int index);

        [DllImport("user32.dll")]
        public static extern nint MonitorFromWindow(nint hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo info);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint hWnd, out Rect rect);
    }

    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MaiScreenWindow";

        private static string ExePath() => Environment.ProcessPath ?? Application.ExecutablePath;

        public static void Apply(bool enable)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;

                if (enable)
                {
                    string command = "\"" + ExePath() + "\"";
                    if (!string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(ValueName, command, RegistryValueKind.String);
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch
            {
                // 注册表不可用时不影响主功能。
            }
        }
    }

    internal sealed class HotkeySpec
    {
        private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["space"] = 0x20,
            ["tab"] = 0x09,
            ["enter"] = 0x0D,
            ["return"] = 0x0D,
            ["esc"] = 0x1B,
            ["escape"] = 0x1B,
            ["backspace"] = 0x08,
            ["insert"] = 0x2D,
            ["ins"] = 0x2D,
            ["delete"] = 0x2E,
            ["del"] = 0x2E,
            ["home"] = 0x24,
            ["end"] = 0x23,
            ["pageup"] = 0x21,
            ["pgup"] = 0x21,
            ["pagedown"] = 0x22,
            ["pgdn"] = 0x22,
            ["up"] = 0x26,
            ["down"] = 0x28,
            ["left"] = 0x25,
            ["right"] = 0x27,
            ["capslock"] = 0x14,
            ["numlock"] = 0x90,
            ["scrolllock"] = 0x91,
            ["pause"] = 0x13,
            ["printscreen"] = 0x2C
        };

        public bool Ctrl { get; private set; }
        public bool Alt { get; private set; }
        public bool Shift { get; private set; }
        public bool Win { get; private set; }
        public int VirtualKey { get; private set; }
        public string Text { get; private set; } = "";

        public bool HasModifier => Ctrl || Alt || Shift || Win;

        public bool IsValid => Text.Length > 0 && (HasModifier || VirtualKey != 0);

        public uint Modifiers
        {
            get
            {
                uint modifiers = Native.ModNoRepeat;
                if (Ctrl) modifiers |= Native.ModControl;
                if (Alt) modifiers |= Native.ModAlt;
                if (Shift) modifiers |= Native.ModShift;
                if (Win) modifiers |= Native.ModWin;
                return modifiers;
            }
        }

        public static HotkeySpec ParseOrFallback(string text)
        {
            return TryParse(text, out HotkeySpec spec) ? spec : new HotkeySpec();
        }

        public static bool TryParse(string? text, out HotkeySpec spec)
        {
            spec = new HotkeySpec();
            if (string.IsNullOrWhiteSpace(text)) return false;

            foreach (string part in text.Split('+'))
            {
                string token = part.Trim();
                if (token.Length == 0) return false;

                if (TryApplyModifier(token, spec)) continue;
                if (spec.VirtualKey != 0 || !TryParseKey(token, out int virtualKey)) return false;
                spec.VirtualKey = virtualKey;
            }

            if (!spec.HasModifier && spec.VirtualKey == 0) return false;
            spec.Text = spec.BuildText();
            return true;
        }

        private static bool TryApplyModifier(string token, HotkeySpec spec)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    spec.Ctrl = true;
                    return true;
                case "alt":
                case "menu":
                    spec.Alt = true;
                    return true;
                case "shift":
                    spec.Shift = true;
                    return true;
                case "win":
                case "windows":
                case "logo":
                case "super":
                    spec.Win = true;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseKey(string token, out int virtualKey)
        {
            virtualKey = 0;

            if (token.Length == 1)
            {
                char c = char.ToUpperInvariant(token[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    virtualKey = c;
                    return true;
                }
            }

            if (token.Length >= 2 && (token[0] == 'f' || token[0] == 'F') &&
                int.TryParse(token.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int functionKey) &&
                functionKey >= 1 && functionKey <= 24)
            {
                virtualKey = 0x70 + functionKey - 1;
                return true;
            }

            if (NamedKeys.TryGetValue(token, out virtualKey)) return true;

            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                Enum.TryParse(token, ignoreCase: true, out Keys key) && key != Keys.None)
            {
                virtualKey = (int)key;
                return true;
            }

            return false;
        }

        public bool IsDown()
        {
            if (Ctrl && !IsKeyDown(Native.VkControl)) return false;
            if (Alt && !IsKeyDown(Native.VkMenu)) return false;
            if (Shift && !IsKeyDown(Native.VkShift)) return false;
            if (Win && !IsKeyDown(Native.VkLWin) && !IsKeyDown(Native.VkRWin)) return false;
            if (VirtualKey != 0 && !IsKeyDown(VirtualKey)) return false;
            return HasModifier || VirtualKey != 0;
        }

        public bool MatchesKeyCode(int virtualKey)
        {
            if (VirtualKey != 0 && virtualKey == VirtualKey) return true;
            if (Ctrl && (virtualKey == Native.VkControl || virtualKey == Native.VkLControl || virtualKey == Native.VkRControl)) return true;
            if (Alt && (virtualKey == Native.VkMenu || virtualKey == Native.VkLMenu || virtualKey == Native.VkRMenu)) return true;
            if (Shift && (virtualKey == Native.VkShift || virtualKey == Native.VkLShift || virtualKey == Native.VkRShift)) return true;
            if (Win && (virtualKey == Native.VkLWin || virtualKey == Native.VkRWin)) return true;
            return false;
        }

        private string BuildText()
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            if (VirtualKey != 0) parts.Add(KeyName(VirtualKey));
            return string.Join("+", parts);
        }

        private static string KeyName(int virtualKey)
        {
            if (virtualKey is >= 'A' and <= 'Z') return ((char)virtualKey).ToString();
            if (virtualKey is >= '0' and <= '9') return ((char)virtualKey).ToString();
            if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x70 + 1);

            return virtualKey switch
            {
                0x0D => "Enter",
                0x1B => "Esc",
                0x2D => "Insert",
                0x2E => "Delete",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x14 => "CapsLock",
                0x90 => "NumLock",
                0x91 => "ScrollLock",
                0x2C => "PrintScreen",
                _ => ((Keys)virtualKey).ToString()
            };
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (Native.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }
    }

    internal sealed class Config
    {
        public const string FileName = "config.ini";

        private const int DefaultX = 300;
        private const int DefaultY = 1600;
        private const int DefaultWidth = 400;
        private const int DefaultHeight = 280;
        private const double DefaultZoom = 1.0;

        private static readonly char[] NumberSeparators = { ',', ';', '，', '；', ' ', '\t' };

        public int X { get; set; } = DefaultX;
        public int Y { get; set; } = DefaultY;
        public int Width { get; set; } = DefaultWidth;
        public int Height { get; set; } = DefaultHeight;
        public double Zoom { get; set; } = DefaultZoom;
        public bool AutoStart { get; set; }

        public HotkeySpec HoldHotkey { get; set; } = HotkeySpec.ParseOrFallback("Alt");
        public HotkeySpec PinHotkey { get; set; } = HotkeySpec.ParseOrFallback("Ctrl+Alt+O");

        public EffectiveArea ValidArea { get; } = new EffectiveArea();
        public bool ValidEnabled { get; set; } = true;
        public bool ValidOutline { get; set; }
        public bool HideInFullscreen { get; set; } = true;

        public List<string> Warnings { get; } = new List<string>();
        public string Path { get; private set; } = "";

        public bool IsValidAreaActive => ValidEnabled && !ValidArea.IsEmpty;

        public bool IsInsideValidArea(int x, int y)
        {
            return IsValidAreaActive && ValidArea.Contains(x, y);
        }

        public static Config Load()
        {
            var config = new Config { Path = ResolvePath() };

            if (File.Exists(config.Path))
            {
                try
                {
                    config.ReadLines(File.ReadAllLines(config.Path));
                }
                catch
                {
                    // 配置读不了时使用默认值，程序仍能启动。
                }
            }

            if (!File.Exists(config.Path))
            {
                config.AddDefaultValidArea();
                config.Save();
            }

            config.Normalize();
            return config;
        }

        internal static Config FromLines(IEnumerable<string> lines, string path = "")
        {
            var config = new Config { Path = path };
            config.ReadLines(lines);
            config.Normalize();
            return config;
        }

        private void ReadLines(IEnumerable<string> lines)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "x":
                        X = ParseInt(value, X);
                        break;
                    case "y":
                        Y = ParseInt(value, Y);
                        break;
                    case "w":
                    case "width":
                        Width = ParseInt(value, Width);
                        break;
                    case "h":
                    case "height":
                        Height = ParseInt(value, Height);
                        break;
                    case "zoom":
                        Zoom = ParseDouble(value, Zoom);
                        break;
                    case "autostart":
                    case "startup":
                    case "runatstartup":
                        AutoStart = ParseBool(value, AutoStart);
                        break;
                    case "holdkey":
                    case "hold.key":
                    case "hotkey.hold":
                        if (HotkeySpec.TryParse(value, out HotkeySpec hold))
                            HoldHotkey = hold;
                        else
                            Warnings.Add("hotkey.hold 格式错误（示例：Alt、Ctrl+Shift）：" + value);
                        break;
                    case "pinhotkey":
                    case "pin.key":
                    case "hotkey.pin":
                        if (HotkeySpec.TryParse(value, out HotkeySpec pin) && pin.VirtualKey != 0)
                            PinHotkey = pin;
                        else
                            Warnings.Add("hotkey.pin 格式错误（示例：Ctrl+Alt+O，必须包含一个主键）：" + value);
                        break;
                    case "valid":
                    case "valid.enabled":
                        ValidEnabled = ParseBool(value, ValidEnabled);
                        break;
                    case "valid.border":
                    case "valid.show":
                    case "valid.outline":
                        ValidOutline = ParseBool(value, ValidOutline);
                        break;
                    case "fullscreen":
                    case "fullscreen.hide":
                    case "hide.fullscreen":
                        HideInFullscreen = ParseBool(value, HideInFullscreen);
                        break;
                    case "valid.rect":
                        AddValidRectangle(value);
                        break;
                    case "valid.circle":
                        AddValidCircle(value);
                        break;
                }
            }
        }
        
        private void AddDefaultValidArea()
        {
            AddValidRectangle("200,100;1062,389");
            AddValidCircle("1062,1367;162,1758;504,1905");
        }

        private void AddValidRectangle(string value)
        {
            if (!TryParseNumbers(value, 4, out double[] values))
            {
                Warnings.Add("valid.rect 格式错误（应为两个对角点：左上x,左上y;右下x,右下y）：" + value);
                return;
            }

            int left = Math.Min(ToInt(values[0]), ToInt(values[2]));
            int right = Math.Max(ToInt(values[0]), ToInt(values[2]));
            int top = Math.Min(ToInt(values[1]), ToInt(values[3]));
            int bottom = Math.Max(ToInt(values[1]), ToInt(values[3]));

            if (right - left < 1 || bottom - top < 1)
            {
                Warnings.Add("valid.rect 宽高必须大于 0：" + value);
                return;
            }

            ValidArea.AddRectangle(new Rectangle(left, top, right - left, bottom - top));
        }

        private void AddValidCircle(string value)
        {
            if (!TryParseNumbers(value, 6, out double[] values))
            {
                Warnings.Add("valid.circle 格式错误（应为 x1,y1;x2,y2;x3,y3）：" + value);
                return;
            }

            var a = new PointF((float)values[0], (float)values[1]);
            var b = new PointF((float)values[2], (float)values[3]);
            var c = new PointF((float)values[4], (float)values[5]);

            if (!CircleGeometry.TryFromThreePoints(a, b, c, out CircleGeometry circle))
            {
                Warnings.Add("valid.circle 三点共线或重合，无法确定圆：" + value);
                return;
            }

            ValidArea.AddCircle(new CircleGeometry(circle.X, circle.Y, circle.Radius, PointsText(values)));
        }

        private static string PointsText(double[] values)
        {
            var text = new StringBuilder();
            for (int i = 0; i < values.Length; i += 2)
            {
                if (i > 0) text.Append(';');
                text.Append(Num(values[i])).Append(',').Append(Num(values[i + 1]));
            }

            return text.ToString();
        }

        private void Normalize()
        {
            Width = Math.Clamp(Width, 80, 4000);
            Height = Math.Clamp(Height, 60, 4000);
            Zoom = Math.Clamp(Zoom, 0.25, 12.0);

            Rectangle screen = SystemInformation.VirtualScreen;
            X = Math.Clamp(X, screen.Left - Width + 120, screen.Right - 120);
            Y = Math.Clamp(Y, screen.Top, screen.Bottom - 60);
        }

        private static int ToInt(double value)
        {
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private static bool TryParseNumbers(string text, int count, out double[] values)
        {
            values = new double[count];
            string[] parts = text.Split(NumberSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != count) return false;

            for (int i = 0; i < count; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    values = new double[count];
                    return false;
                }

                if (Math.Abs(values[i]) > 1_000_000.0) return false;
            }

            return true;
        }

        private static string ResolvePath()
        {
            string exeDirectory = System.IO.Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            DirectoryInfo? directory = new DirectoryInfo(exeDirectory);

            for (int i = 0; i < 5 && directory != null; i++)
            {
                string candidate = System.IO.Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate)) return candidate;

                try
                {
                    if (directory.GetFiles("*.csproj").Length > 0) return candidate;
                }
                catch
                {
                    // 无权限访问目录时继续向上找。
                }

                directory = directory.Parent;
            }

            return System.IO.Path.Combine(exeDirectory, FileName);
        }

        private static int ParseInt(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        private static double ParseDouble(string text, double fallback)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
        }

        private static bool ParseBool(string text, bool fallback)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "1":
                case "on":
                case "yes":
                case "true":
                    return true;
                case "0":
                case "no":
                case "off":
                case "false":
                    return false;
                default:
                    return fallback;
            }
        }

        public void Save()
        {
            try
            {
                var text = new StringBuilder();
                text.AppendLine("; 更改之后托盘右键 -> 重新加载配置（或重启程序）");
                text.AppendLine();
                text.AppendLine("; 按住该键显示小窗");
                text.AppendLine("hotkey.hold=" + HoldHotkey.Text);
                text.AppendLine("; 固定 / 取消固定(全局显示小窗)");
                text.AppendLine("hotkey.pin=" + PinHotkey.Text);
                text.AppendLine();
                text.AppendLine("; 小窗左上角坐标(手台推荐配置)");
                text.AppendLine("x=" + X);
                text.AppendLine("y=" + Y);
                text.AppendLine();
                text.AppendLine("; 小窗大小");
                text.AppendLine("width=" + Width);
                text.AppendLine("height=" + Height);
                text.AppendLine();
                text.AppendLine("; 放大倍数");
                text.AppendLine("zoom=" + Zoom.ToString("0.##", CultureInfo.InvariantCulture));
                text.AppendLine();
                text.AppendLine("; 开机自启：1 打开 / 0 关闭（默认关闭）");
                text.AppendLine("autostart=" + (AutoStart ? "1" : "0"));
                text.AppendLine();
                text.AppendLine("; 以下需配置有效区后启用");
                text.AppendLine();
                text.AppendLine("; 启用有效区：1 启用 / 0 关闭");
                text.AppendLine("valid=" + (ValidEnabled ? "1" : "0"));
                text.AppendLine("; 是否在屏幕上画出有效区边缘：1 显示 / 0 不显示");
                text.AppendLine("valid.outline=" + (ValidOutline ? "1" : "0"));
                text.AppendLine();
                text.AppendLine("; 矩形区域：左上x,左上y;右下x,右下y(无需矩形可在valid前加';'注释)");
                AppendRectangles(text);
                text.AppendLine();
                text.AppendLine("; 圆形：圆周上任意三点x1,y1;x2,y2;x3,y3(无需圆形可在valid前加';'注释)");
                AppendCircles(text);
                text.AppendLine();
                text.AppendLine("; 有全屏程序时不自动显示小窗：1 隐藏 / 0 照常显示");
                text.AppendLine("fullscreen.hide=" + (HideInFullscreen ? "1" : "0"));

                File.WriteAllText(Path, text.ToString());
            }
            catch
            {
                // 配置文件不可写时不影响主功能。
            }
        }

        private void AppendRectangles(StringBuilder text)
        {
            if (ValidArea.Rectangles.Count == 0)
            {
                text.AppendLine("; valid.rect=300,160;900,760");
                return;
            }

            foreach (Rectangle rectangle in ValidArea.Rectangles)
            {
                text.AppendLine("valid.rect=" + rectangle.Left + "," + rectangle.Top + ";" +
                                rectangle.Right + "," + rectangle.Bottom);
            }
        }

        private void AppendCircles(StringBuilder text)
        {
            if (ValidArea.Circles.Count == 0)
            {
                text.AppendLine("; valid.circle=1200,300;1700,600;1400,1000");
                return;
            }

            foreach (CircleGeometry circle in ValidArea.Circles)
            {
                text.AppendLine("valid.circle=" + (circle.Text.Length > 0 ? circle.Text : CircleText(circle)));
            }
        }

        private static string CircleText(CircleGeometry circle)
        {
            var points = new double[3, 2];
            for (int i = 0; i < 3; i++)
            {
                double angle = Math.PI * 2.0 * i / 3.0;
                points[i, 0] = circle.X + circle.Radius * Math.Cos(angle);
                points[i, 1] = circle.Y + circle.Radius * Math.Sin(angle);
            }

            return Num(points[0, 0]) + "," + Num(points[0, 1]) + ";" +
                   Num(points[1, 0]) + "," + Num(points[1, 1]) + ";" +
                   Num(points[2, 0]) + "," + Num(points[2, 1]);
        }

        private static string Num(double value)
        {
            return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class LensForm : Form
    {
        private Config _cfg;
        private readonly Timer _timer = new Timer { Interval = 30 };
        private readonly Font _font = new Font("Consolas", 9f, FontStyle.Bold);

        private Bitmap? _shot;
        private Rectangle _src = Rectangle.Empty;
        private Point _cursor;
        private bool _pinned;
        private bool _active;
        private bool _fullscreen;

        private nint _hook = IntPtr.Zero;
        private Native.LowLevelKeyboardProc? _hookProc;
        private string? _errText;

        private NotifyIcon? _tray;
        private ToolStripMenuItem? _pinMenuItem;
        private AreaOutline? _outline;

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
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            ApplyConfig(cfg);
            Opacity = 0.0;
            _timer.Tick += (_, _) => Tick();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WsExLayered | Native.WsExTransparent |
                              Native.WsExToolWindow | Native.WsExNoActivate;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = TrayText(),
                Visible = true
            };

            var menu = new ContextMenuStrip();
            _pinMenuItem = new ToolStripMenuItem("固定 / 取消固定  (" + _cfg.PinHotkey.Text + ")");
            _pinMenuItem.Click += (_, _) => TogglePin();
            menu.Items.Add(_pinMenuItem);
            menu.Items.Add("重新加载 config.ini", null, (_, _) => ReloadConfig());
            menu.Items.Add("打开 config.ini", null, (_, _) => OpenConfig());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (_, _) => Close());
            _tray.ContextMenuStrip = menu;

            RegisterPinHotkey();
            _hookProc = HookProc;
            _hook = Native.SetWindowsHookEx(Native.WhKeyboardLl, _hookProc,
                                            Native.GetModuleHandle(null), 0);

            ShowConfigWarnings();
            
            Native.GetCursorPos(out Native.Point startPoint);
            _fullscreen = VisibilityPolicy.ShouldYieldToFullscreen(_cfg.IsValidAreaActive, _cfg.HideInFullscreen,
                FullscreenGuard.IsForegroundFullscreen(startPoint.X, startPoint.Y));
            UpdateOutline();
            _timer.Start();
        }

        private string TrayText()
        {
            string hold = _cfg.HoldHotkey.Text;
            string mode = _cfg.IsValidAreaActive
                ? "有效区外自动显示，有效区内按住 " + hold + " 显示"
                : "按住 " + hold + " 放大鼠标附近";
            return "MaiScreenWindow：" + mode + "，" + _cfg.PinHotkey.Text + " 固定";
        }

        private void ApplyConfig(Config cfg)
        {
            _cfg = cfg;
            ClientSize = new Size(cfg.Width, cfg.Height);
            Location = new Point(cfg.X, cfg.Y);

            if (_tray != null) _tray.Text = TrayText();
            if (_pinMenuItem != null) _pinMenuItem.Text = "固定 / 取消固定  (" + cfg.PinHotkey.Text + ")";
            if (IsHandleCreated) UpdateOutline();

            Invalidate();
        }

        private void UpdateOutline()
        {
            bool shouldShow = _cfg.ValidEnabled && _cfg.ValidOutline && !_cfg.ValidArea.IsEmpty && !_fullscreen;
            if (!shouldShow)
            {
                if (_outline is { Visible: true }) _outline.Hide();
                return;
            }

            _outline ??= new AreaOutline();
            _outline.SetShapes(_cfg.ValidArea);
            if (!_outline.Visible) _outline.Show();
        }

        private void RegisterPinHotkey()
        {
            Native.UnregisterHotKey(Handle, Native.HotkeyPin);

            HotkeySpec hotkey = _cfg.PinHotkey;
            if (hotkey.IsValid && hotkey.VirtualKey != 0 &&
                !Native.RegisterHotKey(Handle, Native.HotkeyPin, hotkey.Modifiers, (uint)hotkey.VirtualKey))
            {
                _tray?.ShowBalloonTip(5000, "热键注册失败",
                    "「" + hotkey.Text + "」被其他程序占用。仍可用托盘菜单里的「固定 / 取消固定」。",
                    ToolTipIcon.Warning);
            }
        }

        private nint HookProc(int nCode, nint wParam, nint lParam)
        {
            if (nCode >= 0)
            {
                int message = wParam.ToInt32();
                if (message is Native.WmKeyDown or Native.WmSysKeyDown or
                    Native.WmKeyUp or Native.WmSysKeyUp)
                {
                    Native.KbdLlHookStruct info = Marshal.PtrToStructure<Native.KbdLlHookStruct>(lParam);
                    if (_cfg.HoldHotkey.MatchesKeyCode((int)info.vkCode)) Tick();
                }
            }

            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WmHotkey && m.WParam.ToInt32() == Native.HotkeyPin)
            {
                TogglePin();
                return;
            }

            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
        }

        private void ShowConfigWarnings()
        {
            if (_cfg.Warnings.Count > 0)
            {
                _tray?.ShowBalloonTip(8000, "config.ini 配置有误",
                    string.Join(Environment.NewLine, _cfg.Warnings), ToolTipIcon.Warning);
            }
        }

        private void ReloadConfig()
        {
            Config cfg = Config.Load();
            ApplyConfig(cfg);
            AutoStart.Apply(cfg.AutoStart);
            RegisterPinHotkey();
            ShowConfigWarnings();

            _shot?.Dispose();
            _shot = null;
            Tick();
        }

        private void OpenConfig()
        {
            try
            {
                if (!File.Exists(_cfg.Path)) _cfg.Save();
                Process.Start(new ProcessStartInfo(_cfg.Path) { UseShellExecute = true });
            }
            catch
            {
                // 打不开默认编辑器时不弹出额外错误框。
            }
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
            Native.GetCursorPos(out Native.Point point);
            _cursor = new Point(point.X, point.Y);

            bool holdHeld = _cfg.HoldHotkey.IsDown();
            bool validAreaActive = _cfg.IsValidAreaActive;
            bool insideValidArea = validAreaActive && _cfg.ValidArea.Contains(_cursor.X, _cursor.Y);
            
            bool fullscreen = VisibilityPolicy.ShouldYieldToFullscreen(validAreaActive, _cfg.HideInFullscreen,
                FullscreenGuard.IsForegroundFullscreen(_cursor.X, _cursor.Y));
            if (fullscreen != _fullscreen)
            {
                _fullscreen = fullscreen;
                UpdateOutline();
            }

            if (fullscreen ||
                !VisibilityPolicy.ShouldShow(_pinned, holdHeld, validAreaActive, insideValidArea))
            {
                if (_active)
                {
                    _active = false;
                    Opacity = 0.0;
                }
                return;
            }

            if (!_active)
            {
                _active = true;
                Opacity = 0.97;
                Native.SetWindowPos(Handle, Native.HwndTopmost, 0, 0, 0, 0,
                    Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow);
            }

            GrabScreen();
            _errText = null;
            Invalidate();
        }

        private void GrabScreen()
        {
            double zoom = _cfg.Zoom;
            int width = Math.Max(1, (int)Math.Round(ClientSize.Width / zoom));
            int height = Math.Max(1, (int)Math.Round(ClientSize.Height / zoom));

            Rectangle screen = SystemInformation.VirtualScreen;
            int x = Math.Clamp(_cursor.X - width / 2, screen.Left, Math.Max(screen.Left, screen.Right - width));
            int y = Math.Clamp(_cursor.Y - height / 2, screen.Top, Math.Max(screen.Top, screen.Bottom - height));
            var source = new Rectangle(x, y, width, height);

            if (_shot == null || _shot.Width != width || _shot.Height != height)
            {
                _shot?.Dispose();
                _shot = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            }

            using (Graphics graphics = Graphics.FromImage(_shot))
            {
                graphics.CopyFromScreen(source.X, source.Y, 0, 0, source.Size, CopyPixelOperation.SourceCopy);
            }

            _src = source;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;

            if (_shot != null && _src.Width > 0 && _src.Height > 0)
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.DrawImage(_shot, new Rectangle(Point.Empty, ClientSize));
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            if (_errText != null)
            {
                using var brush = new SolidBrush(Color.FromArgb(255, 255, 120, 120));
                graphics.DrawString("截屏失败：\n" + _errText, _font, brush,
                    new RectangleF(8, 8, ClientSize.Width - 16, ClientSize.Height - 40));
            }

            if (_src.Width > 0 && _src.Height > 0)
            {
                float scaleX = ClientSize.Width / (float)_src.Width;
                float scaleY = ClientSize.Height / (float)_src.Height;
                float cursorX = (_cursor.X - _src.X + 0.5f) * scaleX;
                float cursorY = (_cursor.Y - _src.Y + 0.5f) * scaleY;

                using var cross = new Pen(Color.FromArgb(235, 255, 48, 48), 1.5f);
                graphics.DrawLine(cross, cursorX - 16, cursorY, cursorX + 16, cursorY);
                graphics.DrawLine(cross, cursorX, cursorY - 16, cursorX, cursorY + 16);

                using var ring = new Pen(Color.FromArgb(235, 255, 230, 0), 1.2f);
                graphics.DrawEllipse(ring, cursorX - 5, cursorY - 5, 10, 10);
            }

            string info = _cursor.X + "," + _cursor.Y +
                          "   x" + _cfg.Zoom.ToString("0.##", CultureInfo.InvariantCulture) +
                          (_pinned ? "   PIN" : "");
            SizeF textSize = graphics.MeasureString(info, _font);
            using (var background = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            {
                graphics.FillRectangle(background, 3, ClientSize.Height - textSize.Height - 7,
                    textSize.Width + 8, textSize.Height + 3);
            }

            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            graphics.DrawString(info, _font, Brushes.White, 7, ClientSize.Height - textSize.Height - 5);

            using var border = new Pen(Color.FromArgb(255, 0, 190, 255), 2f);
            graphics.DrawRectangle(border, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (IsHandleCreated) Native.UnregisterHotKey(Handle, Native.HotkeyPin);
                if (_hook != IntPtr.Zero)
                {
                    Native.UnhookWindowsHookEx(_hook);
                    _hook = IntPtr.Zero;
                }

                _timer.Dispose();
                _tray?.Dispose();
                _shot?.Dispose();
                _outline?.Dispose();
                _font.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
