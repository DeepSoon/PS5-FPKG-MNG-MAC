using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FpkgExfat
{
    internal sealed class ModernCard : Panel
    {
        internal Color BorderColor = Color.FromArgb(62, 69, 82);
        internal int BorderWidth = 1;
        internal int Radius = 14;

        internal ModernCard()
        {
            DoubleBuffered = true;
            Padding = new Padding(14);
            Margin = new Padding(6);
            Resize += delegate { UpdateRegion(); };
        }

        private GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, radius * 2);
            if (rectangle.Width <= d || rectangle.Height <= d) { path.AddRectangle(rectangle); return path; }
            path.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateRegion()
        {
            if (Width < 2 || Height < 2) return;
            using (GraphicsPath path = Rounded(new Rectangle(0, 0, Width, Height), Radius)) Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rectangle = ClientRectangle;
            rectangle.Width -= 1; rectangle.Height -= 1;
            using (GraphicsPath path = Rounded(rectangle, Radius))
            using (Pen pen = new Pen(BorderColor, BorderWidth))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}
