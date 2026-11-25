using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScreenLens
{
    internal sealed class AreaOutline : Form
    {
        internal static readonly Color OutlineColor = Color.FromArgb(255, 0, 200, 255);
        private const float OutlineWidth = 4f;

        private EffectiveArea _area = new EffectiveArea();

        public AreaOutline()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
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

        public void SetShapes(EffectiveArea area)
        {
            _area = area;
            Rectangle screen = SystemInformation.VirtualScreen;
            if (Bounds != screen) Bounds = screen;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 不填充默认背景；OnPaint 会先用透明色清屏。
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Draw(e.Graphics, Bounds, _area.Rectangles, _area.Circles);
        }

        internal static void Draw(Graphics g, Rectangle screen,
            IReadOnlyList<Rectangle> rectangles, IReadOnlyList<CircleGeometry> circles)
        {
            if (screen.Width <= 0 || screen.Height <= 0) return;

            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;

            using var pen = new Pen(OutlineColor, OutlineWidth);
            foreach (Rectangle rectangle in rectangles)
            {
                g.DrawRectangle(pen, rectangle.Left - screen.Left, rectangle.Top - screen.Top,
                    rectangle.Width, rectangle.Height);
            }

            foreach (CircleGeometry circle in circles)
            {
                float x = (float)(circle.X - screen.Left - circle.Radius);
                float y = (float)(circle.Y - screen.Top - circle.Radius);
                float diameter = (float)(circle.Radius * 2.0);
                g.DrawEllipse(pen, x, y, diameter, diameter);
            }

            g.SmoothingMode = oldMode;
        }
    }
}
