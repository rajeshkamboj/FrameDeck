using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private sealed class ArtworkTarget
        {
            public string Path;
            public Artwork Picture;
            public Label Title;
            public Label Rating;
            public MovieDetails Movie;
        }

        private MovieCatalog catalog;
        private CancellationTokenSource artworkLoad;
        private Artwork featuredArt;
        private ArtworkTarget featuredTarget;
        private FlowLayoutPanel pager;
        private bool gridNeedsLayout;
        private Size cardSize;
        private int cardGap;

        private void ResizeLibraryGrid()
        {
            if (gridNeedsLayout || !IsHandleCreated || IsDisposed) return;
            gridNeedsLayout = true;
            BeginInvoke(new Action(() => {
                gridNeedsLayout = false;
                if (!closingPlayback && GetPageCapacity() != pageSize) RenderGrid();
            }));
        }

        private int GetPageCapacity()
        {
            int columns = Math.Max(1, grid.ClientSize.Width / (cardSize.Width + cardGap));
            int rows = Math.Max(1, grid.ClientSize.Height / (cardSize.Height + cardGap));
            return columns * rows;
        }

        private Control CreateCard(string path, List<ArtworkTarget> targets)
        {
            var card = new Panel { Size = cardSize, BackColor = Tile, Margin = new Padding(0, 0, cardGap, cardGap) };
            int titleHeight = TextRenderer.MeasureText("Ag", strong).Height + 2;
            int ratingHeight = TextRenderer.MeasureText("Ag", small).Height + 1;
            int footer = Math.Max(titleHeight + ratingHeight + 8, (int)(cardSize.Height * .27));
            var art = new Artwork((path.GetHashCode() & Int32.MaxValue) % 5, false) {
                Dock = DockStyle.Top, Height = cardSize.Height - footer, Cursor = Cursors.Hand
            };
            var title = Caption(MovieName.FromPath(path).Label, strong, Ink);
            title.SetBounds(8, card.Height - footer + 2, card.Width - 43, titleHeight);
            var rating = Caption("", small, Accent);
            rating.SetBounds(8, title.Bottom + 1, card.Width - 43, ratingHeight);
            var target = new ArtworkTarget { Path = path, Picture = art, Title = title, Rating = rating };
            targets.Add(target);
            card.Padding = new Padding(2, 2, 2, 0);
            card.Paint += (sender, args) => {
                if (!StringComparer.OrdinalIgnoreCase.Equals(featured, path)) return;
                using (var outline = new Pen(Accent, 2))
                    args.Graphics.DrawRectangle(outline, 1, 1, card.Width - 3, card.Height - 3);
            };

            var play = MakePlayerButton(PlayerGlyph.Play, "Play " + title.Text, (sender, args) => Play(path, true));
            play.Size = new Size(30, 30);
            play.Location = new Point(card.Width - 34, card.Height - 35);
            var details = new Button {
                FlatStyle = FlatStyle.Flat, Text = "", AccessibleName = "Spotlight for " + title.Text,
                Location = title.Location, Size = title.Size, TabStop = true,
                BackColor = Color.Transparent, ForeColor = Ink
            };
            details.FlatAppearance.BorderSize = 0;
            details.FlatAppearance.MouseOverBackColor = Color.Transparent;
            details.FlatAppearance.MouseDownBackColor = Color.Transparent;
            // A single line with ellipsis keeps the title and rating in separate rows.


            rating.Cursor = Cursors.Hand;
            rating.Click += (sender, args) => SelectSpotlight(target);
            details.Click += (sender, args) => SelectSpotlight(target);
            details.Paint += (sender, args) => TextRenderer.DrawText(args.Graphics, title.Text, title.Font,
                details.ClientRectangle, title.ForeColor, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            title.TextChanged += (sender, args) => {
                details.AccessibleName = "Spotlight for " + title.Text;
                tips.SetToolTip(details, title.Text);
                details.Invalidate();
            };
            tips.SetToolTip(details, MovieName.FromPath(path).Label);
            tips.SetToolTip(art, "Show in spotlight");
            art.Click += (sender, args) => SelectSpotlight(target);
            card.Controls.AddRange(new Control[] { details, rating, play, art });
            card.Disposed += (sender, args) => title.Dispose();
            return card;
        }

        private async void LoadVisibleArtwork(List<ArtworkTarget> targets)
        {
            if (artworkLoad != null) artworkLoad.Cancel();
            var batch = new CancellationTokenSource();
            artworkLoad = batch;
            try { await Task.WhenAll(targets.Select(target => LoadArtwork(target, batch.Token))); }
            finally
            {
                if (artworkLoad == batch)
                {
                    artworkLoad = null;
                    if (!batch.IsCancellationRequested && !closingPlayback && catalog.ProviderIssue != null)
                        mediaCount.Text += "  |  " + catalog.ProviderIssue + " Open Online artwork to test the connection.";
                    if (!batch.IsCancellationRequested && !closingPlayback) RefreshGenreButtons();
                }
                batch.Dispose();
            }
        }

        private async Task LoadArtwork(ArtworkTarget target, CancellationToken token)
        {
            try
            {
                bool isSpotlight = target.Picture == featuredArt;
                using (var result = await catalog.GetAsync(target.Path, token, isSpotlight))
                {
                    if (token.IsCancellationRequested || closingPlayback || target.Picture.IsDisposed) return;
                    target.Movie = result.Movie ?? target.Movie;
                    if (target.Movie != null) movieIndex[target.Path] = target.Movie;
                    target.Picture.SetPicture(result.Image, target.Movie != null && target.Movie.PosterUrl != null);
                    result.Image = null; // The artwork control now owns the bitmap.
                    if (isSpotlight)
                    {
                        target.Picture.SetBackdrop(result.Backdrop);
                        result.Backdrop = null;
                        UpdateSpotlightText(target.Movie);
                    }
                    if (target.Movie == null) return;
                    if (target.Rating != null) target.Rating.Text = target.Movie.RatingLabel;
                    if (target.Title != null)
                        target.Title.Text = target.Movie.Title + (target.Movie.Year == null ? "" : " (" + target.Movie.Year + ")");
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.ExternalException) { }
        }

        private void ShowMovieDetails(ArtworkTarget target)
        {
            if (target == null) return;
            var name = MovieName.FromPath(target.Path);
            var movie = target.Movie;
            string label = movie == null ? name.Label : movie.Title + (movie.Year == null ? "" : " (" + movie.Year + ")");
            using (var dialog = NewArtworkDialog(label, 715, 370))
            {
                var poster = new Artwork(0, false) { Dock = DockStyle.Left, Width = 194, Margin = Padding.Empty };
                poster.SetPicture(target.Picture.CopyPicture(), true);
                var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(18, 0, 0, 0) };
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
                content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
                var headingLabel = Caption(label, heading, Ink);
                headingLabel.Dock = DockStyle.Fill;
                var rating = Caption(movie == null ? "" : movie.RatingLabel, strong, Accent);
                rating.Dock = DockStyle.Fill;
                var plot = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
                    BackColor = Sidebar, ForeColor = Ink, Dock = DockStyle.Fill, Font = regular, TabStop = false };
                plot.Text = movie == null || String.IsNullOrWhiteSpace(movie.Plot)
                    ? catalog.OnlineEnabled ? "A plot summary is unavailable for this video." : "Set up online artwork to look up the movie’s plot and rating."
                    : movie.Plot;
                var actions = Row();
                actions.Controls.Add(Action("Play video", 112, (sender, args) => dialog.DialogResult = DialogResult.OK, true));
                if (movie == null)
                    actions.Controls.Add(Action("Artwork settings", 143, (sender, args) => dialog.DialogResult = DialogResult.Retry));
                else
                    {
                        var attribution = Caption("Details from " + movie.Source, small, Secondary);
                        attribution.Size = new Size(170, 34);
                        actions.Controls.Add(attribution);
                    }
                content.Controls.Add(headingLabel, 0, 0);
                content.Controls.Add(rating, 0, 1);
                content.Controls.Add(plot, 0, 2);
                content.Controls.Add(actions, 0, 3);
                dialog.Controls.Add(content);
                dialog.Controls.Add(poster);
                var result = dialog.ShowDialog(this);
                if (result == DialogResult.OK) Play(target.Path, true);
                if (result == DialogResult.Retry) ConfigureArtwork();
            }
        }

        private Form NewArtworkDialog(string title, int width, int height)
        {
            return new Form { Text = title, ClientSize = new Size(width, height), Padding = new Padding(18),
                MinimizeBox = false, MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, BackColor = Sidebar, ForeColor = Ink,
                Font = regular };
        }

        private void ConfigureArtwork()
        {
            using (var dialog = NewArtworkDialog("Online artwork", 592, 391))
            {
                var description = Caption("Use an OMDb key for posters, plots and IMDb ratings. A TMDB token is optional and adds a second source for posters and movie details.", regular, Ink);
                description.SetBounds(18, 12, 555, 61);
                dialog.Controls.Add(description);
                var omdb = ArtworkCredential(dialog, "OMDb API key", catalog.OmdbKey, "https://www.omdbapi.com/apikey.aspx", 85);
                var tmdb = ArtworkCredential(dialog, "TMDB Read Access Token or API key", catalog.TmdbToken, "https://www.themoviedb.org/settings/api", 155);
                var connection = Caption("Test both keys and the poster download without changing your settings.", small, Secondary);
                connection.SetBounds(18, 215, 555, 51);
                dialog.Controls.Add(connection);
                var notice = Caption("This product uses the TMDB API but is not endorsed or certified by TMDB.\nMovie data and IMDb ratings provided by OMDb when available.", small, Secondary);
                notice.SetBounds(18, 273, 555, 50);
                dialog.Controls.Add(notice);
                var save = Action("Save and load", 129, (sender, args) => dialog.DialogResult = DialogResult.OK, true);
                save.Location = new Point(18, 337);
                dialog.Controls.Add(save);
                var cancel = Action("Cancel", 81, (sender, args) => dialog.DialogResult = DialogResult.Cancel);
                cancel.Location = new Point(284, 337);
                dialog.Controls.Add(cancel);
                using (var lifetime = new CancellationTokenSource())
                {
                    Button test = Action("Test connection", 122, async (sender, args) => {
                        var button = (Button)sender;
                        button.Enabled = false;
                        connection.Text = "Checking OMDb, TMDB and their poster hosts...";
                        try
                        {
                            string result = await catalog.TestConnections(tmdb.Text.Trim(), omdb.Text.Trim(), lifetime.Token);
                            if (!dialog.IsDisposed && dialog.Visible) connection.Text = result;
                        }
                        catch (OperationCanceledException) { }
                        finally { if (!button.IsDisposed) button.Enabled = true; }
                    });
                    test.Location = new Point(154, 337);
                    dialog.Controls.Add(test);
                    dialog.AcceptButton = save;
                    dialog.CancelButton = cancel;
                    var confirmed = dialog.ShowDialog(this) == DialogResult.OK;
                    lifetime.Cancel();
                    if (!confirmed) return;
                }

                if (artworkLoad != null) artworkLoad.Cancel();
                try { catalog.SaveCredentials(tmdb.Text.Trim(), omdb.Text.Trim()); }
                catch (IOException) { ShowArtworkSettingsError(); return; }
                catch (UnauthorizedAccessException) { ShowArtworkSettingsError(); return; }
                catch (CryptographicException) { ShowArtworkSettingsError(); return; }
                if (genreIndexLoad != null) genreIndexLoad.Cancel();
                movieIndex.Clear();
                UpdateSpotlight(featured, refresh: true);
                RenderGrid();
                IndexGenres();
            }
        }

        private TextBox ArtworkCredential(Form dialog, string label, string value, string link, int top)
        {
            var caption = Caption(label, strong, Ink);
            caption.SetBounds(18, top, 330, 24);
            var entry = new TextBox { UseSystemPasswordChar = true, Text = value, AccessibleName = label };
            entry.SetBounds(18, top + 28, 430, 26);
            var get = Action("Get key", 104, (sender, args) =>
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }));
            get.SetBounds(464, top + 24, 104, 31);
            dialog.Controls.AddRange(new Control[] { caption, entry, get });
            return entry;
        }

        private void ShowArtworkSettingsError()
        {
            MessageBox.Show(this, "FrameDeck could not save the artwork settings on this PC.",
                "Online artwork", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
