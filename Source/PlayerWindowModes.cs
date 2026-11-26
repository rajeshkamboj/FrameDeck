using System;
using System.Drawing;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private bool miniPlayer;
        private Rectangle mainBounds;
        private FormWindowState mainState;
        private Size mainMinimum;
        private Rectangle miniBounds;
        private bool cleanView;
        private PlayerButton miniButton;
        private double GetDeviceScale()
        {
            using (var graphics = CreateGraphics()) return graphics.DpiX / 96d;
        }

        private void ToggleMiniPlayer()
        {
            if (miniPlayer)
            {
                LeaveMiniPlayer();
                return;
            }
            if (section != 2 || activeFile == null || closingPlayback) return;
            if (fullScreen) ToggleFullScreen();
            trackMenu.Close();

            var screenArea = Screen.FromControl(this).WorkingArea;
            mainState = WindowState;
            mainBounds = mainState == FormWindowState.Normal ? Bounds : RestoreBounds;
            mainMinimum = MinimumSize;
            miniPlayer = true;
            double scale = GetDeviceScale();
            if (miniBounds.IsEmpty || !screenArea.IntersectsWith(miniBounds))
            {
                int width = (int)Math.Round(440 * scale);
                int height = (int)Math.Round(285 * scale);
                miniBounds = new Rectangle(screenArea.Right - width - 16, screenArea.Bottom - height - 16, width, height);
            }

            SuspendLayout();
            WindowState = FormWindowState.Normal;
            MinimumSize = new Size((int)(330 * scale), (int)(202 * scale));
            FormBorderStyle = cleanView ? FormBorderStyle.None : FormBorderStyle.SizableToolWindow;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ApplyPlaybackLayout();
            Bounds = miniBounds;
            ResumeLayout(true);
        }

        private void LeaveMiniPlayer()
        {
            if (!miniPlayer) return;
            miniBounds = Bounds;
            miniPlayer = false;
            SuspendLayout();
            TopMost = false;
            FormBorderStyle = cleanView ? FormBorderStyle.None : FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            MinimumSize = mainMinimum;
            ApplyPlaybackLayout();
            Bounds = mainBounds;
            WindowState = mainState;
            ResumeLayout(true);
        }

        private void ToggleCleanView()
        {
            if (section != 2 || closingPlayback || (!cleanView && !attemptedPlayback)) return;
            SetCleanView(!cleanView);
        }

        private void SetCleanView(bool enabled)
        {
            if (cleanView == enabled) return;
            trackMenu.Close();
            cleanView = enabled;
            SuspendLayout();
            if (!fullScreen)
            {
                Rectangle bounds = Bounds;
                FormBorderStyle = enabled ? FormBorderStyle.None :
                    miniPlayer ? FormBorderStyle.SizableToolWindow : FormBorderStyle.Sizable;
                if (WindowState == FormWindowState.Normal) Bounds = bounds;
            }
            ApplyPlaybackLayout();
            ResumeLayout(true);
            if (enabled) ActiveControl = null;
        }

        private void ApplyPlaybackLayout()
        {
            bool showNavigation = !miniPlayer && !fullScreen && !cleanView;
            navigation.Visible = showNavigation;
            shell.ColumnStyles[0].Width = showNavigation ? 216 : 0;
            player.Padding = showNavigation ? new Padding(22, 16, 22, 13) :
                cleanView ? Padding.Empty : new Padding(6, 0, 6, 4);
            player.RowStyles[0].Height = showNavigation ? 48 : 0;
            player.RowStyles[2].Height = cleanView ? 0 : 36;
            player.RowStyles[3].Height = cleanView ? 0 : 39;
            progressPanel.Visible = !cleanView;
            playbackActions.Visible = !cleanView;
            stop.Visible = !miniPlayer;
            audio.Visible = !miniPlayer;
            subtitles.Visible = !miniPlayer;
            screen.Visible = !miniPlayer;
            SetPlayerGlyph(screen, fullScreen ? PlayerGlyph.Window : PlayerGlyph.Fullscreen,
                fullScreen ? "Exit fullscreen (F)" : "Fullscreen (F)");
            SetPlayerGlyph(miniButton, miniPlayer ? PlayerGlyph.Restore : PlayerGlyph.Mini,
                miniPlayer ? "Restore main window (P)" : "Mini player (P)");
        }
    }
}
