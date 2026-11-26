using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed class Artwork : Panel
    {
        private readonly int colorIndex;
        private readonly bool featured;

        private Bitmap picture;
        private bool contain;

        private Bitmap backdrop;

        public void SetBackdrop(Bitmap value)
        {
            if (backdrop != null) backdrop.Dispose();
            backdrop = value;
            Invalidate();
        }

        private void PaintSpotlight(Graphics graphics)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.Clear(Color.FromArgb(26, 36, 51));
            if (backdrop != null)
            {
                float scale = Math.Max(Width / (float)backdrop.Width, Height / (float)backdrop.Height);
                float width = backdrop.Width * scale, height = backdrop.Height * scale;
                graphics.DrawImage(backdrop, (Width - width) / 2, (Height - height) / 2, width, height);
            }
            using (var fade = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(255, 26, 36, 51), Color.FromArgb(46, 14, 19, 29), 0f))
                graphics.FillRectangle(fade, ClientRectangle);

            if (picture == null) return;
            float margin = Math.Max(7, Height * .045f);
            float widthLimit = Width * .78f;
            float posterScale = Math.Min(widthLimit / picture.Width, (Height - margin * 2) / picture.Height);
            float posterWidth = picture.Width * posterScale, posterHeight = picture.Height * posterScale;
            float x = Width - posterWidth - Math.Max(margin, Width * .08f);
            float y = (Height - posterHeight) / 2;
            using (var shadow = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                graphics.FillRectangle(shadow, x - 3, y - 3, posterWidth + 8, posterHeight + 8);
            graphics.DrawImage(picture, x, y, posterWidth, posterHeight);
        }


        public void SetPicture(Bitmap value, bool preferContain)
        {
            if (picture != null) picture.Dispose();
            picture = value;
            contain = preferContain || (value != null && value.Height > value.Width);
            Invalidate();
        }

        public Bitmap CopyPicture() { return picture == null ? null : new Bitmap(picture); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (picture != null) { picture.Dispose(); picture = null; }
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            }
            base.Dispose(disposing);
        }

        public Artwork(int colorIndex, bool featured)
        {
            this.colorIndex = colorIndex;
            this.featured = featured;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            var graphics = e.Graphics;
            if (featured && (picture != null || backdrop != null))
            {
                PaintSpotlight(graphics);
                base.OnPaint(e);
                return;
            }
            if (picture != null)
            {
                graphics.Clear(Color.FromArgb(17, 25, 37));
                float horizontal = Width / (float)picture.Width;
                float vertical = Height / (float)picture.Height;
                float scale = contain ? Math.Min(horizontal, vertical) : Math.Max(horizontal, vertical);
                float width = picture.Width * scale, height = picture.Height * scale;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (contain && width < Width)
                {
                    // Let the poster's colors fill the extra width while keeping the full poster sharp.
                    using (var blur = new Bitmap(picture, 20, 20))
                    using (var shade = new SolidBrush(Color.FromArgb(168, 14, 19, 29)))
                    {
                        graphics.DrawImage(blur, ClientRectangle);
                        graphics.FillRectangle(shade, ClientRectangle);
                    }
                }
                graphics.DrawImage(picture, (Width - width) / 2, (Height - height) / 2, width, height);
                base.OnPaint(e);
                return;
            }
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var colors = new[] { Color.FromArgb(36, 100, 113), Color.FromArgb(83, 64, 128),
                Color.FromArgb(44, 94, 139), Color.FromArgb(122, 75, 75), Color.FromArgb(43, 109, 92) };
            var hue = colors[colorIndex % colors.Length];
            using (var background = new LinearGradientBrush(ClientRectangle,
                Color.FromArgb(17, 25, 37), hue, 19f))
                graphics.FillRectangle(background, ClientRectangle);

            float span = Math.Min(Width, Height);
            var center = new PointF(Width * (featured ? .63f : .65f), Height * .54f);
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(center.X - span, center.Y - span, span * 2, span * 2);
                using (var fill = new PathGradientBrush(halo))
                {
                    fill.CenterColor = Color.FromArgb(110, 97, 221, 219);
                    fill.SurroundColors = new[] { Color.FromArgb(0, hue) };
                    graphics.FillPath(fill, halo);
                }
            }

            var transform = graphics.Save();
            graphics.TranslateTransform(center.X, center.Y);
            graphics.RotateTransform(-22);
            for (int i = 0; i < 5; i++)
            {
                float width = span * (.56f + i * .26f);
                float height = width * (.58f + i * .032f);
                var frame = new RectangleF(-width / 2, -height / 2, width, height);
                using (var pen = new Pen(Color.FromArgb(140 - i * 24, 160, 239, 235), featured ? 1.7f : 1f))
                    graphics.DrawRectangle(pen, frame.X, frame.Y, frame.Width, frame.Height);
            }
            graphics.Restore(transform);

            if (featured)
            {
                using (var pen = new Pen(Color.FromArgb(190, 218, 255, 250), 2))
                {
                    float radius = span * .125f;
                    graphics.DrawEllipse(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2);
                    graphics.DrawPolygon(pen, new[] {
                        new PointF(center.X - radius * .24f, center.Y - radius * .46f),
                        new PointF(center.X + radius * .49f, center.Y),
                        new PointF(center.X - radius * .24f, center.Y + radius * .46f)
                    });
                }
            }
            base.OnPaint(e);
        }
    }
}
