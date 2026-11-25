using System;
using System.Collections.Generic;
using System.Drawing;

namespace ScreenLens
{
    internal readonly struct CircleGeometry(double x, double y, double radius, string text = "")
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Radius { get; } = radius;

        /// <summary>配置里原始的“三点”文本，写回配置文件时原样保留。</summary>
        public string Text { get; } = text;

        public bool Contains(double x, double y)
        {
            if (Radius <= 0) return false;
            double dx = x - X;
            double dy = y - Y;
            return dx * dx + dy * dy <= Radius * Radius;
        }
        
        public static bool TryFromThreePoints(PointF a, PointF b, PointF c, out CircleGeometry circle)
        {
            circle = default;

            double ax = a.X, ay = a.Y;
            double bx = b.X, by = b.Y;
            double cx = c.X, cy = c.Y;

            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            if (Math.Abs(d) < 1e-9) return false;

            double a2 = ax * ax + ay * ay;
            double b2 = bx * bx + by * by;
            double c2 = cx * cx + cy * cy;

            double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            double radius = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
            if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)) return false;

            circle = new CircleGeometry(ux, uy, radius);
            return true;
        }
    }

    /// <summary>有效区：长方形与圆形的并集。</summary>
    internal sealed class EffectiveArea
    {
        private readonly List<Rectangle> _rectangles = new List<Rectangle>();
        private readonly List<CircleGeometry> _circles = new List<CircleGeometry>();

        public IReadOnlyList<Rectangle> Rectangles => _rectangles;
        public IReadOnlyList<CircleGeometry> Circles => _circles;
        public bool IsEmpty => _rectangles.Count == 0 && _circles.Count == 0;

        public void AddRectangle(Rectangle rectangle)
        {
            if (rectangle.Width > 0 && rectangle.Height > 0) _rectangles.Add(rectangle);
        }

        public void AddCircle(CircleGeometry circle)
        {
            if (circle.Radius > 0) _circles.Add(circle);
        }

        public bool Contains(double x, double y)
        {
            foreach (Rectangle rectangle in _rectangles)
            {
                if (x >= rectangle.Left && x <= rectangle.Right &&
                    y >= rectangle.Top && y <= rectangle.Bottom)
                {
                    return true;
                }
            }

            foreach (CircleGeometry circle in _circles)
            {
                if (circle.Contains(x, y)) return true;
            }

            return false;
        }
    }

    /// <summary>小窗显隐规则。</summary>
    internal static class VisibilityPolicy
    {
        public static bool ShouldShow(bool pinned, bool holdHeld, bool validAreaActive, bool insideValidArea)
        {
            if (pinned) return true;
            if (!validAreaActive) return holdHeld;
            return !insideValidArea || holdHeld;
        }
    }
}
