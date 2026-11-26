using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FrameDeck
{
    internal enum PlayerGlyph { Play, Pause, Stop, Captions, Audio, Volume, Muted, Fullscreen, Window, Mini, Restore, Clean }

    internal sealed class PlayerButton : Button
    {
        private PlayerGlyph glyph;

        public void SetGlyph(PlayerGlyph value, string label)
        {
            if (glyph == value && AccessibleName == label) return;
            glyph = value;
            AccessibleName = label;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var graphic = e.Graphics;
            var original = graphic.Save();
            graphic.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Max(1f, graphic.DpiX / 96f);
            graphic.TranslateTransform((Width - 20 * scale) / 2, (Height - 20 * scale) / 2);
            graphic.ScaleTransform(scale, scale);
            Color color = Enabled ? ForeColor : Color.FromArgb(115, ForeColor);
            using (var pen = new Pen(color, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case PlayerGlyph.Play:
                        graphic.FillPolygon(fill, new[] { new Point(6, 3), new Point(6, 17), new Point(17, 10) });
                        break;
                    case PlayerGlyph.Pause:
                        graphic.FillRectangle(fill, 5, 4, 3, 12);
                        graphic.FillRectangle(fill, 12, 4, 3, 12);
                        break;
                    case PlayerGlyph.Stop:
                        graphic.FillRectangle(fill, 4, 4, 12, 12);
                        break;
                    case PlayerGlyph.Captions:
                        graphic.DrawRectangle(pen, 2, 4, 16, 12);
                        graphic.DrawLine(pen, 5, 9, 9, 9);
                        graphic.DrawLine(pen, 11, 9, 15, 9);
                        graphic.DrawLine(pen, 5, 13, 11, 13);
                        graphic.DrawLine(pen, 13, 13, 15, 13);
                        break;
                    case PlayerGlyph.Audio:
                        graphic.DrawLines(pen, new[] { new Point(7, 14), new Point(7, 5), new Point(16, 3), new Point(16, 12) });
                        graphic.FillEllipse(fill, 2, 12, 6, 5);
                        graphic.FillEllipse(fill, 11, 10, 6, 5);
                        break;
                    case PlayerGlyph.Volume:
                    case PlayerGlyph.Muted:
                        graphic.DrawPolygon(pen, new[] { new Point(2, 7), new Point(5, 7), new Point(9, 3),
                            new Point(9, 17), new Point(5, 13), new Point(2, 13) });
                        if (glyph == PlayerGlyph.Muted)
                        {
                            graphic.DrawLine(pen, 12, 7, 18, 13);
                            graphic.DrawLine(pen, 12, 13, 18, 7);
                        }
                        else
                        {
                            graphic.DrawArc(pen, 8, 6, 6, 8, -58, 116);
                            graphic.DrawArc(pen, 8, 3, 10, 14, -58, 116);
                        }
                        break;
                    case PlayerGlyph.Fullscreen:
                    case PlayerGlyph.Window:
                        for (int i = 0; i < 4; i++)
                        {
                            if (glyph == PlayerGlyph.Fullscreen)
                                graphic.DrawLines(pen, new[] { new Point(2, 7), new Point(2, 2), new Point(7, 2) });
                            else
                                graphic.DrawLines(pen, new[] { new Point(2, 7), new Point(7, 7), new Point(7, 2) });
                            graphic.TranslateTransform(10, 10);
                            graphic.RotateTransform(90);
                            graphic.TranslateTransform(-10, -10);
                        }
                        break;
                    case PlayerGlyph.Mini:
                    case PlayerGlyph.Restore:
                        graphic.DrawRectangle(pen, 2, 3, 16, 13);
                        if (glyph == PlayerGlyph.Mini) graphic.FillRectangle(fill, 10, 9, 7, 6);
                        else graphic.DrawLines(pen, new[] { new Point(7, 12), new Point(13, 6), new Point(8, 6) });
                        break;
                    case PlayerGlyph.Clean:
                        graphic.DrawRectangle(pen, 2, 4, 16, 12);
                        graphic.DrawLine(pen, 3, 17, 17, 3);
                        break;
                }
            }
            graphic.Restore(original);
        }
    }

    internal sealed partial class DeckWindow
    {
        private PlayerButton MakePlayerButton(PlayerGlyph glyph, string label, EventHandler click, bool primary = false)
        {
            var button = new PlayerButton {
                Size = new Size(38, 34), Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat,
                ForeColor = primary ? Color.FromArgb(11, 39, 42) : Ink,
                BackColor = primary ? Accent : Hover, Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(163, 249, 236) : Color.FromArgb(52, 76, 91);
            button.Click += click;
            SetPlayerGlyph(button, glyph, label);
            return button;
        }

        private void SetPlayerGlyph(PlayerButton button, PlayerGlyph glyph, string label)
        {
            if (button.AccessibleName == label) return;
            button.SetGlyph(glyph, label);
            tips.SetToolTip(button, label);
        }

        private void SetPlayGlyph(bool playing)
        {
            SetPlayerGlyph(playPause, playing ? PlayerGlyph.Pause : PlayerGlyph.Play, playing ? "Pause (Space)" : "Play (Space)");
        }
    }
}
