using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ScreenLens
{
    internal static class CursorIcon
    {
        private const uint CursorShowing = 0x00000001;
        private const uint DiNormal = 0x0003;
        private const int SmCxCursor = 13;
        private const int SmCyCursor = 14;
        private const int DefaultCursorSize = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int cbSize;
            public uint flags;
            public IntPtr hCursor;
            public Point ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public int fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo iconInfo);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon,
            int cx, int cy, int step, IntPtr flickerFree, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
        
        internal static Rectangle Placement(float cursorX, float cursorY, float scale,
            int hotspotX, int hotspotY, int cursorWidth, int cursorHeight)
        {
            int width = Math.Max(1, (int)Math.Round(cursorWidth * scale));
            int height = Math.Max(1, (int)Math.Round(cursorHeight * scale));
            int x = (int)Math.Round(cursorX - hotspotX * scale);
            int y = (int)Math.Round(cursorY - hotspotY * scale);
            return new Rectangle(x, y, width, height);
        }
        
        internal static bool Draw(Graphics graphics, float cursorX, float cursorY, float scale)
        {
            var info = new CursorInfo { cbSize = Marshal.SizeOf<CursorInfo>() };
            if (!GetCursorInfo(ref info) || info.hCursor == IntPtr.Zero || (info.flags & CursorShowing) == 0)
                return false;

            if (!GetIconInfo(info.hCursor, out IconInfo icon))
                return false;

            try
            {
                int cursorWidth = GetSystemMetrics(SmCxCursor);
                int cursorHeight = GetSystemMetrics(SmCyCursor);
                if (cursorWidth <= 0) cursorWidth = DefaultCursorSize;
                if (cursorHeight <= 0) cursorHeight = DefaultCursorSize;

                return DrawCursor(graphics, cursorX, cursorY, scale, info.hCursor,
                    icon.xHotspot, icon.yHotspot, cursorWidth, cursorHeight);
            }
            finally
            {
                if (icon.hbmMask != IntPtr.Zero) DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) DeleteObject(icon.hbmColor);
            }
        }
        
        internal static bool DrawCursor(Graphics graphics, float cursorX, float cursorY, float scale,
            IntPtr hCursor, int hotspotX, int hotspotY, int cursorWidth, int cursorHeight)
        {
            Rectangle box = Placement(cursorX, cursorY, scale, hotspotX, hotspotY, cursorWidth, cursorHeight);
            IntPtr hdc = graphics.GetHdc();
            try
            {
                return DrawIconEx(hdc, box.X, box.Y, hCursor, box.Width, box.Height, 0, IntPtr.Zero, DiNormal);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
    }
}
