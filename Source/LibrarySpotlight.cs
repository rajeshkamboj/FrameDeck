using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private readonly Dictionary<string, MovieDetails> movieIndex =
            new Dictionary<string, MovieDetails>(StringComparer.OrdinalIgnoreCase);
        private readonly FlowLayoutPanel genreBar = new FlowLayoutPanel();
        private readonly ContextMenuStrip genreMenu = new ContextMenuStrip();
        private readonly Label featuredPlot = new Label();
        private CancellationTokenSource genreIndexLoad, spotlightLoad;
        private Button featuredDetails;
        private string selectedGenre;
        private string genreButtonState;
        private string spotlightSelection;

        private void AddSidebarCredits()
        {
            var credits = new TableLayoutPanel {
                Dock = DockStyle.Bottom, Height = 88, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(22, 8, 10, 14), AccessibleName = "Designed by RAJESH KAMBOJ. Powered by PixlDot."
            };
            for (int i = 0; i < 3; i++) credits.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3f));
            string[] lines = { "Designed by", "RAJESH KAMBOJ", "Powered by PixlDot" };
            for (int i = 0; i < lines.Length; i++)
            {
                var label = Caption(lines[i], i == 1 ? strong : small, i == 1 ? Ink : i == 2 ? Accent : Secondary);
                label.Dock = DockStyle.Fill;
                label.Margin = Padding.Empty;
                credits.Controls.Add(label, 0, i);
            }
            navigation.Controls.Add(credits);
            var shortcuts = Caption("Ctrl+O  Open video\nF11       Full screen", small, Secondary);
            shortcuts.AutoEllipsis = false;
            shortcuts.SetBounds(22, 0, 174, 44);
            Action placeShortcuts = () => {
                shortcuts.Top = credits.Top - shortcuts.Height - 8;
                shortcuts.Visible = shortcuts.Top >= navigation.Controls.Cast<Control>()
                    .Where(control => control is Button).Max(control => control.Bottom) + 16;
            };
            navigation.Controls.Add(shortcuts);
            navigation.Layout += (sender, args) => placeShortcuts();
            placeShortcuts();
        }

        private IEnumerable<string> VideosForSection()
        {
            return section == 0 ? paths : state.Recent.Where(System.IO.File.Exists);
        }

        private bool MatchesGenre(string path)
        {
            if (selectedGenre == null) return true;
            MovieDetails movie;
            return movieIndex.TryGetValue(path, out movie) && movie != null && movie.Genres != null &&
                movie.Genres.Contains(selectedGenre, StringComparer.OrdinalIgnoreCase);
        }

        private void ChooseGenre(string value)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(selectedGenre, value)) return;
            selectedGenre = value;
            page = 0;
            spotlightSelection = null;
            RenderGrid();
        }

        private void BuildGenreBar()
        {
            genreBar.Dock = DockStyle.Fill;
            genreBar.Margin = new Padding(0, 0, 0, 5);
            genreBar.Padding = new Padding(0, 1, 0, 0);
            genreBar.WrapContents = false;
            genreBar.AccessibleName = "Filter videos by genre";
            genreBar.SizeChanged += (sender, args) => RefreshGenreButtons();
            library.Controls.Add(genreBar, 0, 1);
        }

        private void RefreshGenreButtons()
        {
            if (section == 2 || closingPlayback || genreMenu.Visible || genreBar.Width < 180) return;
            var available = VideosForSection().Where(movieIndex.ContainsKey)
                .Select(path => movieIndex[path]).Where(movie => movie != null && movie.Genres != null)
                .SelectMany(movie => movie.Genres).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(genre => genre, StringComparer.CurrentCultureIgnoreCase).ToList();
            var quick = new[] { "Action", "Adventure", "Drama", "Horror", "Sci-fi", "Thriller" }
                .Concat(available).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(genre => available.Contains(genre, StringComparer.OrdinalIgnoreCase)).ToList();
            if (selectedGenre != null && quick.Remove(selectedGenre)) quick.Insert(0, selectedGenre);
            string layout = genreBar.ClientSize.Width + "|" + selectedGenre + "|" + String.Join("|", quick);
            if (layout == genreButtonState) return;
            genreButtonState = layout;
            genreBar.SuspendLayout();
            while (genreBar.Controls.Count > 0) genreBar.Controls[0].Dispose();
            genreBar.Controls.Add(GenreButton("All genres", null));
            int used = genreBar.Controls[0].Width + genreBar.Controls[0].Margin.Horizontal;
            var remaining = new List<string>();
            for (int i = 0; i < quick.Count; i++)
            {
                Button button = GenreButton(quick[i], quick[i]);
                int width = button.Width + button.Margin.Horizontal;
                int reserve = i < quick.Count - 1 ? 84 : 0;
                if (used + width + reserve <= genreBar.ClientSize.Width)
                {
                    genreBar.Controls.Add(button);
                    used += width;
                }
                else
                {
                    button.Dispose();
                    remaining.Add(quick[i]);
                }
            }
            if (remaining.Count != 0)
            {
                Button more = Action("More", 71, (sender, args) => {
                    if (genreMenu.Visible) return;
                    while (genreMenu.Items.Count > 0) genreMenu.Items[0].Dispose();
                    foreach (string value in remaining)
                        genreMenu.Items.Add(value, null, (item, evt) => BeginInvoke(new Action(() => {
                            if (!closingPlayback && section != 2) ChooseGenre(value);
                        })));
                    genreMenu.Show((Control)sender, new Point(0, ((Control)sender).Height));
                });
                more.Height = 30;
                more.AccessibleName = "More genres";
                genreBar.Controls.Add(more);
            }
            genreBar.ResumeLayout();
        }

        private Button GenreButton(string label, string value)
        {
            bool selected = StringComparer.OrdinalIgnoreCase.Equals(value, selectedGenre);
            int width = TextRenderer.MeasureText(label, strong).Width + 26;
            Button button = Action(label, width, (sender, args) => BeginInvoke(new Action(() => {
                if (!closingPlayback && section != 2) ChooseGenre(value);
            })), selected);
            button.Height = 30;
            button.TabStop = true;
            button.BackColor = selected ? Accent : Background;
            button.FlatAppearance.BorderSize = selected ? 0 : 1;
            button.FlatAppearance.BorderColor = Hover;
            button.AccessibleName = label;
            return button;
        }

        private async void IndexGenres()
        {
            if (genreIndexLoad != null) genreIndexLoad.Cancel();
            if (closingPlayback || !catalog.OnlineEnabled) return;
            var batch = new CancellationTokenSource();
            genreIndexLoad = batch;
            var pending = state.Recent.Where(System.IO.File.Exists).Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !movieIndex.ContainsKey(path)).ToArray();
            try
            {
                foreach (string path in pending)
                {
                    batch.Token.ThrowIfCancellationRequested();
                    if (movieIndex.ContainsKey(path)) continue;
                    MovieDetails movie = await catalog.GetMetadataAsync(path, batch.Token);
                    if (!batch.IsCancellationRequested && movie != null) movieIndex[path] = movie;
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (genreIndexLoad == batch)
                {
                    genreIndexLoad = null;
                    if (!batch.IsCancellationRequested && section != 2 && !closingPlayback)
                    {
                        if (selectedGenre == null) RefreshGenreButtons();
                        else RenderGrid();
                    }
                }
                batch.Dispose();
            }
        }

        private string PickSpotlight(string[] items)
        {
            if (spotlightSelection != null && items.Contains(spotlightSelection, StringComparer.OrdinalIgnoreCase))
                return spotlightSelection;
            return state.Recent.FirstOrDefault(path => state.Positions.ContainsKey(path) &&
                items.Contains(path, StringComparer.OrdinalIgnoreCase)) ?? items.FirstOrDefault();
        }

        private void SelectSpotlight(ArtworkTarget target)
        {
            spotlightSelection = target.Path;
            UpdateSpotlight(target.Path, target);
            foreach (Panel card in grid.Controls.OfType<Panel>()) card.Invalidate();
        }

        private void UpdateSpotlight(string path, ArtworkTarget card = null, bool refresh = false)
        {
            featured = path;
            if (!refresh && featuredTarget != null && StringComparer.OrdinalIgnoreCase.Equals(featuredTarget.Path, path))
            {
                UpdateSpotlightText(featuredTarget.Movie);
                return;
            }
            if (spotlightLoad != null) spotlightLoad.Cancel();
            featuredArt.SetPicture(card == null ? null : card.Picture.CopyPicture(), false);
            featuredArt.SetBackdrop(null);
            MovieDetails metadata = null;
            if (path != null) movieIndex.TryGetValue(path, out metadata);
            featuredTarget = path == null ? null : new ArtworkTarget { Path = path, Picture = featuredArt, Movie = metadata };
            UpdateSpotlightText(metadata);
            if (featuredTarget != null) LoadSpotlight(featuredTarget);
        }

        private async void LoadSpotlight(ArtworkTarget target)
        {
            var batch = new CancellationTokenSource();
            spotlightLoad = batch;
            try { await LoadArtwork(target, batch.Token); }
            finally
            {
                if (spotlightLoad == batch) spotlightLoad = null;
                batch.Dispose();
            }
        }

        private void UpdateSpotlightText(MovieDetails movie)
        {
            if (featured == null)
            {
                featuredTitle.Text = "Play a video";
                featuredCaption.Text = "Open a file, or add a folder to browse your videos.";
                featuredPlot.Text = "";
                featuredAction.Text = "Open video";
                featuredDetails.Visible = false;
            }
            else
            {
                var name = MovieName.FromPath(featured);
                long position;
                bool resume = state.Positions.TryGetValue(featured, out position);
                featuredTitle.Text = movie == null || String.IsNullOrWhiteSpace(movie.Title) ? name.Title : movie.Title;
                var terms = new List<string> { movie == null ? name.Year : movie.Year ?? name.Year };
                if (movie != null)
                {
                    if (movie.Genres != null && movie.Genres.Length > 0) terms.Add(String.Join(" / ", movie.Genres.Take(2)));
                    terms.Add(movie.RatingLabel);
                }
                if (resume) terms.Add(FormatTime(position));
                featuredCaption.Text = String.Join("  ·  ", terms.Where(text => !String.IsNullOrWhiteSpace(text)));
                featuredPlot.Text = movie == null ? "" : movie.Plot ?? "";
                featuredAction.Text = resume ? "Resume" : "Play";
                featuredDetails.Visible = true;
            }
            tips.SetToolTip(featuredTitle, featuredTitle.Text);
            tips.SetToolTip(featuredPlot, featuredPlot.Text);
        }
    }
}
