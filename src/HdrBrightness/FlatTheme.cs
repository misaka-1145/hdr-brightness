using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HdrBrightness
{
    /// <summary>扁平化主题：一套颜色 + 字体 + 圆角绘制工具。</summary>
    internal static class FlatTheme
    {
        // 背景 / 表面
        public static readonly Color PageBackground = Color.FromArgb(0xF5, 0xF6, 0xF8);
        public static readonly Color Surface = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color SurfaceSubtle = Color.FromArgb(0xF1, 0xF3, 0xF6);
        public static readonly Color SurfaceHover = Color.FromArgb(0xE9, 0xEC, 0xF1);
        public static readonly Color Border = Color.FromArgb(0xEB, 0xEE, 0xF2);
        public static readonly Color BorderStrong = Color.FromArgb(0xDE, 0xE3, 0xEA);

        // 文字
        public static readonly Color TextPrimary = Color.FromArgb(0x1B, 0x1F, 0x27);
        public static readonly Color TextSecondary = Color.FromArgb(0x76, 0x7E, 0x8A);
        public static readonly Color TextMuted = Color.FromArgb(0x9A, 0xA1, 0xAC);

        // 主色
        public static readonly Color Accent = Color.FromArgb(0x2F, 0x6F, 0xED);
        public static readonly Color AccentText = Color.FromArgb(0x1F, 0x51, 0xC0);
        public static readonly Color AccentSoft = Color.FromArgb(0xEA, 0xF1, 0xFE);

        // 状态色
        public static readonly Color Green = Color.FromArgb(0x18, 0x9B, 0x5B);
        public static readonly Color GreenSoft = Color.FromArgb(0xE6, 0xF6, 0xEE);
        public static readonly Color Amber = Color.FromArgb(0xC4, 0x7D, 0x14);
        public static readonly Color AmberSoft = Color.FromArgb(0xFF, 0xF5, 0xE6);
        public static readonly Color Violet = Color.FromArgb(0x6D, 0x55, 0xE0);
        public static readonly Color VioletSoft = Color.FromArgb(0xF0, 0xEC, 0xFE);

        private static readonly string Family = "Microsoft YaHei UI";

        public static readonly Font Title = new Font(Family, 14F);
        public static readonly Font Body = new Font(Family, 9F);
        public static readonly Font BodyBold = new Font(Family, 9F, FontStyle.Bold);
        public static readonly Font CardTitle = new Font(Family, 10.5F);
        public static readonly Font Value = new Font(Family, 15F);
        public static readonly Font Small = new Font(Family, 8F);
        public static readonly Font SmallBold = new Font(Family, 8F, FontStyle.Bold);
        public static readonly Font Section = new Font(Family, 9F, FontStyle.Bold);

        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = radius * 2;
            if (radius <= 0 || diameter > bounds.Width || diameter > bounds.Height)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color color)
        {
            using (var path = RoundedPath(bounds, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        public static void DrawRounded(Graphics g, Rectangle bounds, int radius, Color color, float width)
        {
            using (var path = RoundedPath(bounds, radius))
            using (var pen = new Pen(color, width))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>按控件当前 DPI 缩放像素值（字号用点，不需要缩放）。</summary>
        public static int Scale(Control control, int pixels)
        {
            float scale = 96f;
            if (control != null && control.DeviceDpi > 0) scale = control.DeviceDpi;
            return (int)System.Math.Round(pixels * scale / 96f);
        }
    }
}
