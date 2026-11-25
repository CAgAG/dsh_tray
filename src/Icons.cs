using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace DshTray
{
    /// <summary>Draws the little circular tray glyphs, one color per DSH state.</summary>
    internal static class Icons
    {
        public static Icon ForState(DshState state)
        {
            Color color;
            switch (state)
            {
                case DshState.Running: color = Color.FromArgb(34, 197, 94); break;
                case DshState.External: color = Color.FromArgb(14, 165, 233); break;
                case DshState.Starting:
                case DshState.Stopping: color = Color.FromArgb(245, 158, 11); break;
                case DshState.Error:
                case DshState.Conflict: color = Color.FromArgb(239, 68, 68); break;
                default: color = Color.FromArgb(148, 163, 184); break;
            }
            return Draw(color);
        }

        private static Icon Draw(Color color)
        {
            int size = SystemInformation.SmallIconSize.Width;
            if (size < 16) size = 16;
            if (size > 64) size = 64;

            using (Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    float pad = size * 0.05f;
                    RectangleF circle = new RectangleF(pad, pad, size - pad * 2f, size - pad * 2f);
                    using (SolidBrush brush = new SolidBrush(color)) graphics.FillEllipse(brush, circle);

                    float ringWidth = Math.Max(1f, size * 0.055f);
                    using (Pen ring = new Pen(Color.FromArgb(70, Color.White), ringWidth))
                    {
                        graphics.DrawEllipse(ring, circle);
                    }

                    float stroke = Math.Max(1.7f, size * 0.115f);
                    using (Pen pen = new Pen(Color.White, stroke))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        PointF[] points = new PointF[]
                        {
                            new PointF(size * 0.36f, size * 0.29f),
                            new PointF(size * 0.60f, size * 0.50f),
                            new PointF(size * 0.36f, size * 0.71f)
                        };
                        graphics.DrawLines(pen, points);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
                finally
                {
                    Native.DestroyIcon(handle);
                }
            }
        }
    }
}
