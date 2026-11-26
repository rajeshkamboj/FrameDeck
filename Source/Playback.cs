using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private VlcEngine engine;
        private string activeFile;
        private long pendingResume;
        private long lastPositionSave;
        private bool muted;
        private bool attemptedPlayback;
        private bool reachedPlayback;
        private bool draggingSeek;
        private bool seeking;
        private bool closingPlayback;
        private bool playbackDisposed;
        private bool seekable;
        private long duration;
        private long elapsed;
        private long? queuedSeek;
        private long seekTarget;
        private Action afterSeek;
        private bool soundChangedDuringSeek;
        private bool pauseAfterSeek;
        private string[] matchingSubtitles = new string[0];
        private readonly Dictionary<string, int> loadedSubtitles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool addingSubtitle;
        private int playbackGeneration;
        private readonly Timer seekDelay = new Timer { Interval = 120 };
        private readonly Timer clock = new Timer { Interval = 240 };
        private readonly Label playbackTitle = new Label();
        private readonly Label status = new Label();
        private readonly Label time = new Label();
        private readonly TrackBar timeline = new TrackBar();
        private PlayerButton playPause, stop, mute, subtitles, audio, screen, cleanButton;
        private Panel progressPanel;
        private FlowLayoutPanel playbackActions;
        private TrackBar volume;
        private readonly ContextMenuStrip trackMenu = new ContextMenuStrip();

        private void BuildPlayer()
        {
            player.Dock = DockStyle.Fill;
            player.BackColor = Background;
            player.Padding = new Padding(22, 16, 22, 13);
            player.ColumnCount = 1;
            player.RowCount = 4;
            player.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            player.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            player.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            player.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            body.Controls.Add(player);

            FormatLabel(playbackTitle, heading, Ink);
            playbackTitle.Dock = DockStyle.Fill;
            playbackTitle.Margin = Padding.Empty;
            playbackTitle.Text = "Now playing";
            player.Controls.Add(playbackTitle, 0, 0);

            var stage = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.Black };
            video.BackColor = Color.Black;
            video.Dock = DockStyle.Fill;
            video.MouseWheel += ForwardPlaybackWheel;
            MouseWheel += ForwardPlaybackWheel;
            video.DoubleClick += (sender, args) => ToggleFullScreen();
            FormatLabel(status, strong, Secondary);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.BackColor = Background;
            status.Text = "Open a video to start watching.";
            status.Dock = DockStyle.Fill;
            stage.Controls.Add(video);
            stage.Controls.Add(status);
            player.Controls.Add(stage, 0, 1);

            progressPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4, 2, 8, 0) };
            timeline.Dock = DockStyle.Fill;
            timeline.Maximum = 1000;
            timeline.AutoSize = false;
            timeline.TickStyle = TickStyle.None;
            timeline.BackColor = Background;
            timeline.AccessibleName = "Playback position";
            timeline.MouseDown += (sender, args) => { if (args.Button == MouseButtons.Left) draggingSeek = true; };
            timeline.MouseUp += (sender, args) => { if (args.Button == MouseButtons.Left) FinishDrag(); };
            timeline.MouseCaptureChanged += (sender, args) => {
                if (draggingSeek && !timeline.Capture) FinishDrag();
            };
            timeline.Scroll += (sender, args) => {
                if (draggingSeek) PreviewSlider();
                else SeekToSlider();
            };
            seekDelay.Tick += (sender, args) => CommitSeek();
            FormatLabel(time, small, Secondary);
            time.Dock = DockStyle.Right;
            time.Width = 140;
            time.TextAlign = ContentAlignment.MiddleRight;
            time.Text = "0:00 / 0:00";
            progressPanel.Controls.Add(timeline);
            progressPanel.Controls.Add(time);
            player.Controls.Add(progressPanel, 0, 2);

            playbackActions = Row();
            playbackActions.Padding = new Padding(4, 1, 0, 0);
            playPause = MakePlayerButton(PlayerGlyph.Play, "Play (Space)", (sender, args) => TogglePause(), true);
            stop = MakePlayerButton(PlayerGlyph.Stop, "Stop (S)", (sender, args) => StopPlayback());
            subtitles = MakePlayerButton(PlayerGlyph.Captions, "Subtitle tracks (V to cycle)", (sender, args) => ShowTrackMenu(subtitles, true));
            audio = MakePlayerButton(PlayerGlyph.Audio, "Audio tracks (B to cycle)", (sender, args) => ShowTrackMenu(audio, false));
            mute = MakePlayerButton(PlayerGlyph.Volume, "Mute (M)", (sender, args) => ToggleMute());
            screen = MakePlayerButton(PlayerGlyph.Fullscreen, "Fullscreen (F)", (sender, args) => ToggleFullScreen());
            miniButton = MakePlayerButton(PlayerGlyph.Mini, "Mini player (P)", (sender, args) => ToggleMiniPlayer());
            cleanButton = MakePlayerButton(PlayerGlyph.Clean, "Video only (I or right-click to restore)",
                (sender, args) => ToggleCleanView());
            volume = new TrackBar {
                Maximum = 100, Value = state.Volume, Width = 124, Height = 34, AutoSize = false, TickStyle = TickStyle.None,
                Margin = new Padding(0, 0, 6, 0), BackColor = Background, AccessibleName = "Volume"
            };
            tips.SetToolTip(volume, "Volume (mouse wheel or Up / Down)");
            volume.Scroll += (sender, args) => ChangeVolume(volume.Value);
            playbackActions.Controls.AddRange(new Control[] { playPause, stop, subtitles, audio, mute, volume, screen, miniButton, cleanButton });
            player.Controls.Add(playbackActions, 0, 3);

            clock.Tick += (sender, args) => UpdatePlayback();
            SetPlaybackControls(false);
        }

        private void Play(string path, bool resume)
        {
            if (!closingPlayback) WhenSeekEnds(() => OpenPlayback(path, resume));
        }

        private void OpenPlayback(string path, bool resume)
        {
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "This file could not be found.", "FrameDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            ShowSection(2);
            if (engine == null)
            {
                try { engine = new VlcEngine(video.Handle); }
                catch (InvalidOperationException ex) { ShowPlaybackMessage(ex.Message); return; }
                catch (DllNotFoundException) { ShowPlaybackMessage("Install 64-bit VLC 3 from videolan.org, then reopen FrameDeck."); return; }
                catch (BadImageFormatException) { ShowPlaybackMessage("FrameDeck requires the 64-bit VLC 3 engine."); return; }
            }

            if (attemptedPlayback)
            {
                SavePosition();
                DetachRendererInput();
                engine.Stop();
            }
            activeFile = path;
            playbackTitle.Text = DisplayName(path);
            pendingResume = 0;
            if (resume) state.Positions.TryGetValue(path, out pendingResume);
            reachedPlayback = false;
            seekable = false;
            duration = elapsed = 0;
            playbackGeneration++;
            addingSubtitle = false;
            soundChangedDuringSeek = false;
            loadedSubtitles.Clear();
            matchingSubtitles = SubtitleFiles.Find(path);
            pauseAfterSeek = false;
            aspectIndex = 0;
            engine.SetAspectRatio(null);
            attemptedPlayback = engine.Play(path);
            lastPositionSave = ClockMilliseconds();
            timeline.Value = 0;
            time.Text = "0:00 / 0:00";
            if (!attemptedPlayback)
            {
                ShowPlaybackMessage("FrameDeck could not open this video.");
                return;
            }
            status.Text = "Opening video…";
            status.Visible = true;
            status.BringToFront();
            SetPlaybackControls(true);
            clock.Start();
        }

        private void UpdatePlayback()
        {
            if (closingPlayback || !attemptedPlayback || seeking) return;
            int current = engine.State;
            if (current == 6 || current == 7)
            {
                if (current == 6)
                {
                    state.Positions.Remove(activeFile);
                    SaveState();
                }
                ShowPlaybackMessage(current == 6 ? "Playback finished" : "FrameDeck could not decode this video.");
                playPause.Enabled = current == 6;
                return;
            }

            if (current == 3 || current == 4)
            {
                if (!reachedPlayback)
                {
                    reachedPlayback = true;
                    state.Record(activeFile);
                    SaveState();
                    engine.SetVolume(state.Volume);
                    engine.SetMute(muted);
                    status.Visible = false;
                    video.BringToFront();
                    if (matchingSubtitles.Length > 0) LoadSubtitle(matchingSubtitles[0], false);
                }
                AttachRendererInput();
                elapsed = Math.Max(0, engine.Time);
                duration = Math.Max(0, engine.Length);
                seekable = duration > 0 && engine.IsSeekable;
                timeline.Enabled = seekable;
                if (pendingResume > 0 && seekable)
                {
                    long resume = pendingResume;
                    pendingResume = 0;
                    if (resume < duration - 1500) Seek(resume);
                }
            }

            if (!draggingSeek && !queuedSeek.HasValue)
            {
                timeline.Value = duration > 0 ? Math.Min(timeline.Maximum, (int)(elapsed * (double)timeline.Maximum / duration)) : 0;
                time.Text = FormatTime(elapsed) + " / " + FormatTime(duration);
            }

            SetPlayGlyph(current != 4);
            if (reachedPlayback && ClockMilliseconds() - lastPositionSave >= 5000)
            {
                lastPositionSave = ClockMilliseconds();
                SavePosition();
            }
        }

        private static long ClockMilliseconds() { return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond; }

        private void SavePosition()
        {
            if (!attemptedPlayback || !reachedPlayback || engine == null) return;
            long elapsed = engine.Time;
            long duration = engine.Length;
            if (elapsed < 0) return;
            if (elapsed >= 1000 && (duration <= 0 || elapsed < duration - 1500))
            {
                long saved;
                if (state.Positions.TryGetValue(activeFile, out saved) && elapsed == saved) return;
                state.Positions[activeFile] = elapsed;
            }
            else if (!state.Positions.Remove(activeFile)) return;
            SaveState();
        }

        private void ShowPlaybackMessage(string message)
        {
            DetachRendererInput();
            attemptedPlayback = false;
            clock.Stop();
            status.Text = message;
            status.Visible = true;
            status.BringToFront();
            SetPlaybackControls(false);
        }

        private void SetPlaybackControls(bool enabled)
        {
            SetPlayGlyph(enabled);
            playPause.Enabled = enabled;
            cleanButton.Enabled = enabled;
            miniButton.Enabled = enabled || activeFile != null;
            stop.Enabled = enabled;
            subtitles.Enabled = enabled;
            audio.Enabled = enabled;
            timeline.Enabled = enabled && seekable;
        }

        private void StopPlayback()
        {
            if (!closingPlayback) WhenSeekEnds(StopMedia);
        }

        private void StopMedia()
        {
            if (!attemptedPlayback) return;
            SavePosition();
            engine.Stop();
            ShowPlaybackMessage("Playback stopped");
            playPause.Enabled = true;
        }

        private void TogglePause()
        {
            if (afterSeek != null || closingPlayback) return;
            if (seeking) { pauseAfterSeek = !pauseAfterSeek; return; }
            if (attemptedPlayback) engine.TogglePause();
            else if (activeFile != null) Play(activeFile, true);
        }

        private void FinishDrag()
        {
            if (!draggingSeek) return;
            draggingSeek = false;
            SeekToSlider();
        }

        private long SliderTime()
        {
            return (long)(duration * ((double)timeline.Value / timeline.Maximum));
        }

        private void PreviewSlider()
        {
            time.Text = FormatTime(SliderTime()) + " / " + FormatTime(duration);
        }

        private void SeekToSlider() { Seek(SliderTime()); }

        private void Seek(long target)
        {
            if (!attemptedPlayback || !seekable || closingPlayback || afterSeek != null) return;
            queuedSeek = Math.Min(duration - 1, Math.Max(0, target));
            time.Text = FormatTime(queuedSeek.Value) + " / " + FormatTime(duration);
            seekDelay.Stop();
            if (!seeking) seekDelay.Start();
        }

        private async void CommitSeek()
        {
            seekDelay.Stop();
            if (!queuedSeek.HasValue || !attemptedPlayback || seeking) return;
            seekTarget = queuedSeek.Value;
            queuedSeek = null;
            seeking = true;
            long target = seekTarget;
            VlcEngine current = engine;
            try
            {
                // VLC may wait for its renderer while seeking. Keep the window message pump free.
                await Task.Run(() => current.Seek(target));
            }
            finally
            {
                seeking = false;
                Action next = afterSeek;
                afterSeek = null;
                if (next != null) next();
                else
                {
                    if (soundChangedDuringSeek && attemptedPlayback)
                    {
                        engine.SetVolume(state.Volume);
                        engine.SetMute(muted);
                    }
                    soundChangedDuringSeek = false;
                    if (pauseAfterSeek)
                    {
                        pauseAfterSeek = false;
                        if (attemptedPlayback) engine.TogglePause();
                    }
                    if (queuedSeek.HasValue) seekDelay.Start();
                }
            }
        }

        private void CancelQueuedSeek()
        {
            seekDelay.Stop();
            queuedSeek = null;
            draggingSeek = false;
        }

        private void WhenSeekEnds(Action action)
        {
            CancelQueuedSeek();
            if (seeking) afterSeek = action;
            else action();
        }


        private void ChangeVolume(int value)
        {
            state.Volume = Math.Min(volume.Maximum, Math.Max(volume.Minimum, value));
            volume.Value = state.Volume;
            if (seeking) soundChangedDuringSeek = true;
            else if (engine != null && attemptedPlayback) engine.SetVolume(state.Volume);
            SaveState();
        }

        private void ToggleMute()
        {
            muted = !muted;
            SetPlayerGlyph(mute, muted ? PlayerGlyph.Muted : PlayerGlyph.Volume, muted ? "Unmute (M)" : "Mute (M)");
            if (seeking) soundChangedDuringSeek = true;
            else if (engine != null && attemptedPlayback) engine.SetMute(muted);
        }

        private static readonly string[] AspectRatios = { null, "16:9", "4:3", "1:1", "16:10", "2.21:1", "2.35:1", "2.39:1", "5:4" };
        private int aspectIndex;

        private void CycleAspectRatio()
        {
            if (!attemptedPlayback || !reachedPlayback || seeking) return;
            aspectIndex = (aspectIndex + 1) % AspectRatios.Length;
            engine.SetAspectRatio(AspectRatios[aspectIndex]);
        }

        private void CycleTrack(bool captions)
        {
            if (!attemptedPlayback || !reachedPlayback || seeking) return;
            var tracks = engine.GetTracks(captions).Where(track => track.Id >= 0).ToList();
            if (tracks.Count == 0) return;
            int current = engine.CurrentTrack(captions);
            int index = tracks.FindIndex(track => track.Id == current);
            int next = index + 1;
            if (next == tracks.Count && captions) engine.SelectTrack(true, -1);
            else engine.SelectTrack(captions, tracks[next % tracks.Count].Id);
        }


        private void ShowTrackMenu(Button button, bool captions)
        {
            if (closingPlayback || !attemptedPlayback || seeking || afterSeek != null) return;
            // WinForms still uses the dropdown and its items while dispatching Closed.
            // Keep it owned by the window and rebuild the items before the next Show.
            if (trackMenu.Visible) return;
            while (trackMenu.Items.Count > 0) trackMenu.Items[0].Dispose();
            int generation = playbackGeneration;
            int current = engine.CurrentTrack(captions);
            var tracks = engine.GetTracks(captions);
            foreach (var track in tracks)
            {
                int id = track.Id;
                var item = new ToolStripMenuItem(id < 0 ? "Off" : String.IsNullOrEmpty(track.Name) ? "Track " + id : track.Name) {
                    Checked = id == current
                };
                item.Click += (sender, args) => {
                    if (CanUseTrackMenu(generation) && !seeking) engine.SelectTrack(captions, id);
                };
                trackMenu.Items.Add(item);
            }
            if (!tracks.Any(track => track.Id >= 0))
                trackMenu.Items.Add(new ToolStripMenuItem(captions ? "No loaded subtitle tracks" : "No audio tracks") { Enabled = false });
            if (captions)
            {
                trackMenu.Items.Add(new ToolStripSeparator());
                foreach (string path in matchingSubtitles.Take(15))
                {
                    var item = new ToolStripMenuItem(Path.GetFileName(path).Replace("&", "&&")) {
                        ToolTipText = path
                    };
                    int id;
                    item.Checked = loadedSubtitles.TryGetValue(path, out id) && id == current;
                    item.Click += (sender, args) => {
                        if (CanUseTrackMenu(generation)) LoadSubtitle(path);
                    };
                    trackMenu.Items.Add(item);
                }
                trackMenu.Items.Add("Browse for subtitle file", null, (sender, args) => {
                    if (!CanUseTrackMenu(generation)) return;
                    BeginInvoke(new Action(() => {
                        if (CanUseTrackMenu(generation)) ChooseSubtitle(generation);
                    }));
                });
            }
            trackMenu.Show(button, new Point(0, button.Height));
        }

        private bool CanUseTrackMenu(int generation)
        {
            return !closingPlayback && !IsDisposed && IsHandleCreated &&
                attemptedPlayback && generation == playbackGeneration;
        }

        private void ChooseSubtitle(int generation)
        {
            if (!attemptedPlayback || activeFile == null || addingSubtitle) return;
            using (var dialog = new OpenFileDialog { Title = "Open subtitles", Filter = SubtitleFiles.DialogFilter,
                InitialDirectory = Path.GetDirectoryName(activeFile), CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK && CanUseTrackMenu(generation)) LoadSubtitle(dialog.FileName);
        }

        private async void LoadSubtitle(string path, bool reportFailure = true)
        {
            if (!attemptedPlayback || addingSubtitle || closingPlayback) return;
            if (seeking)
            {
                if (afterSeek == null) WhenSeekEnds(() => LoadSubtitle(path, reportFailure));
                return;
            }

            int generation = playbackGeneration;
            int id;
            var before = new HashSet<int>(engine.GetTracks(true).Select(track => track.Id));
            if (loadedSubtitles.TryGetValue(path, out id) && before.Contains(id) && engine.SelectTrack(true, id)) return;
            bool accepted = engine.AddSubtitle(path);
            if (accepted)
            {
                addingSubtitle = true;
                try
                {
                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        await Task.Delay(100);
                        if (IsDisposed || closingPlayback || generation != playbackGeneration || !attemptedPlayback) return;
                        if (seeking) continue;
                        var added = engine.GetTracks(true).FirstOrDefault(track => track.Id >= 0 && !before.Contains(track.Id));
                        if (added == null) continue;
                        loadedSubtitles[path] = added.Id;
                        if (engine.CurrentTrack(true) != added.Id) engine.SelectTrack(true, added.Id);
                        return;
                    }
                }
                finally { if (generation == playbackGeneration) addingSubtitle = false; }
            }

            if (reportFailure) MessageBox.Show(this, "VLC could not load this subtitle file:\n" + path,
                "FrameDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }


        private void ToggleFullScreen()
        {
            if (section != 2 || closingPlayback) return;
            SuspendLayout();
            if (fullScreen)
            {
                fullScreen = false;
                FormBorderStyle = cleanView ? FormBorderStyle.None : FormBorderStyle.Sizable;
                Bounds = windowBounds;
                WindowState = windowState;
            }
            else
            {
                if (miniPlayer) LeaveMiniPlayer();
                var display = System.Windows.Forms.Screen.FromControl(this);
                windowState = WindowState;
                windowBounds = windowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = FormWindowState.Normal;
                fullScreen = true;
                FormBorderStyle = FormBorderStyle.None;
                Bounds = display.Bounds;
            }
            ApplyPlaybackLayout();
            ResumeLayout(true);
        }


        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O)) { ChooseVideo(); return true; }
            if (keyData == (Keys.Control | Keys.F))
            {
                if (fullScreen) ToggleFullScreen();
                if (section == 2) ShowSection(0);
                search.Focus();
                search.SelectAll();
                return true;
            }
            if (HandlePlaybackKey(keyData, false)) return true;
            return base.ProcessCmdKey(ref message, keyData);
        }

        private static string FormatTime(long milliseconds)
        {
            var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return elapsed.TotalHours >= 1 ? String.Format("{0}:{1:00}:{2:00}", (int)elapsed.TotalHours, elapsed.Minutes, elapsed.Seconds) :
                String.Format("{0}:{1:00}", elapsed.Minutes, elapsed.Seconds);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel || playbackDisposed) return;
            if (!closingPlayback && miniPlayer && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                LeaveMiniPlayer();
                return;
            }
            closingPlayback = true;
            if (seeking)
            {
                e.Cancel = true;
                WhenSeekEnds(Close);
                return;
            }
            playbackDisposed = true;
            if (artworkLoad != null) artworkLoad.Cancel();
            if (genreIndexLoad != null) genreIndexLoad.Cancel();
            if (spotlightLoad != null) spotlightLoad.Cancel();
            genreMenu.Close();
            genreMenu.Dispose();
            Application.RemoveMessageFilter(this);
            if (scan != null) scan.Cancel();
            clock.Stop();
            CancelQueuedSeek();
            SavePosition();
            DetachRendererInput();
            attemptedPlayback = false;
            trackMenu.Close();
            trackMenu.Dispose();
            if (engine != null) { engine.Dispose(); engine = null; }
            seekDelay.Dispose();
            clock.Dispose();
            tips.Dispose();
        }
    }
}
