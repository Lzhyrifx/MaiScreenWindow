using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace ScreenLens
{
    internal static class FullscreenGuard
    {
        private const int GwlStyle = -16;
        private const long WsCaption = 0x00C00000L;

        public static bool IsForegroundFullscreen(int cursorX, int cursorY)
        {
            nint foreground = Native.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            Native.GetWindowThreadProcessId(foreground, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false; // 小窗和蓝色边缘是自己
            if (!Native.IsWindowVisible(foreground)) return false;
            if (IsShellWindow(foreground)) return false;

            nint monitor = Native.MonitorFromWindow(foreground, Native.MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero) return false;

            var info = new Native.MonitorInfo { cbSize = Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) return false;

            Rectangle screen = info.Monitor.ToRectangle();
            if (!screen.Contains(cursorX, cursorY)) return false; // 全屏程序在别的屏幕，不影响本屏幕的小窗

            if (!Native.GetWindowRect(foreground, out Native.Rect rect)) return false;

            bool hasCaption = (Native.GetWindowLong(foreground, GwlStyle) & WsCaption) != 0;
            return CoversMonitor(rect.ToRectangle(), screen, hasCaption);
        }


        internal static bool CoversMonitor(Rectangle window, Rectangle monitor, bool hasCaption)
        {
            if (hasCaption) return false;
            return window.Left <= monitor.Left && window.Top <= monitor.Top &&
                   window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        }

        private static bool IsShellWindow(nint hwnd)
        {
            var name = new StringBuilder(64);
            Native.GetClassName(hwnd, name, name.Capacity);
            return name.ToString() switch
            {
                "Progman" => true,
                "WorkerW" => true,
                "Shell_TrayWnd" => true,
                "Shell_SecondaryTrayWnd" => true,
                _ => false
            };
        }
    }
}
