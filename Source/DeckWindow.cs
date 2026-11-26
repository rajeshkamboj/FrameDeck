using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow : Form
    {
        private static readonly Color Background = Color.FromArgb(14, 19, 29);
        private static readonly Color Sidebar = Color.FromArgb(18, 25, 37);
        private static readonly Color Tile = Color.FromArgb(26, 36, 51);
        private static readonly Color Hover = Color.FromArgb(38, 54, 69);
        private static readonly Color Ink = Color.FromArgb(239, 245, 250);
        private static readonly Color Secondary = Color.FromArgb(163, 183, 201);
        private static readonly Color Accent = Color.FromArgb(118, 232, 215);
        private readonly Font regular = new Font("Segoe UI", 10);
        private readonly Font strong = new Font("Segoe UI Semibold", 10);
        private readonly Font small = new Font("Segoe UI", 9);
        private readonly Font heading = new Font("Segoe UI Semibold", 25);
        private readonly Font wordmark = new Font("Segoe UI Semibold", 18);
        private readonly ToolTip tips = new ToolTip();

        private readonly LibraryState state;
        private readonly string statePath;
        private readonly TableLayoutPanel shell = new TableLayoutPanel();
        private readonly Panel navigation = new Panel();
        private readonly Panel body = new Panel();
        private readonly TableLayoutPanel library = new TableLayoutPanel();
        private readonly TableLayoutPanel player = new TableLayoutPanel();
        private readonly Panel video = new Panel();
        private readonly FlowLayoutPanel grid = new FlowLayoutPanel();
        private readonly TextBox search = new TextBox();
        private readonly Label libraryTitle = new Label();
        private readonly Label mediaCount = new Label();
        private TableLayoutPanel featuredBody;
        private double spotlightScale = 1;
        private readonly Label featuredTitle = new Label();
        private readonly Label featuredCaption = new Label();
        private readonly Label pageText = new Label();
        private Button libraryNav, recentNav, playerNav, previousPage, nextPage, featuredAction;

        private string[] paths = new string[0];
        private CancellationTokenSource scan;
        private bool scanning;
        private int page, pageSize, section;
        private bool fullScreen;
        private Rectangle windowBounds;
        private FormWindowState windowState;
        private string featured;
        private string initialFile;

        public DeckWindow(string initialFile, string statePath)
        {
            this.initialFile = initialFile;
            this.statePath = statePath;
            state = LibraryState.Load(statePath);
            catalog = new MovieCatalog(Path.Combine(Path.GetDirectoryName(statePath), "artwork"));
            cardSize = new Size(148, 156);
            cardGap = 12;
            Text = "FrameDeck";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(875, 620);
            Size = new Size(1210, 795);
            BackColor = Background;
            ForeColor = Ink;
            Font = regular;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;
            AllowDrop = true;
            DragEnter += (sender, args) => {
                if (args.Data.GetDataPresent(DataFormats.FileDrop)) args.Effect = DragDropEffects.Copy;
            };
            DragDrop += (sender, args) => {
                var files = args.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                var directories = files.Where(Directory.Exists).ToArray();
                if (directories.Length > 0) AddFolders(directories);
                string file = files.FirstOrDefault(File.Exists);
                if (file != null) Play(file, true);
            };

            shell.Dock = DockStyle.Fill;
            shell.ColumnCount = 2;
            shell.RowCount = 1;
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 216));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.Controls.Add(navigation, 0, 0);
            shell.Controls.Add(body, 1, 0);
            navigation.Dock = DockStyle.Fill;
            navigation.Margin = Padding.Empty;
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            Controls.Add(shell);

            BuildNavigation();
            BuildLibrary();
            BuildPlayer();
            Application.AddMessageFilter(this);
            ShowSection(0);
            Shown += (sender, args) => {
                var scale = GetDeviceScale();
                spotlightScale = scale;
                int titleHeight = Math.Max((int)Math.Round(48 * scale), TextRenderer.MeasureText("Ag", heading).Height + 6);
                int captionHeight = Math.Max((int)Math.Round(32 * scale), TextRenderer.MeasureText("Ag", small).Height * 2);
                featuredBody.RowStyles[0].Height = titleHeight;
                featuredBody.RowStyles[1].Height = captionHeight;
                ResizeSpotlight();
                cardSize = new Size((int)Math.Round(148 * scale), (int)Math.Round(156 * scale));
                cardGap = (int)Math.Round(12 * scale);
                ScanFolders();
                if (!String.IsNullOrEmpty(this.initialFile))
                {
                    string file = this.initialFile;
                    this.initialFile = null;
                    Play(file, true);
                }
            };
        }

        private void ResizeSpotlight()
        {
            if (featuredBody == null) return;
            float minimum = (float)(198 * spotlightScale);
            float maximum = (float)(285 * spotlightScale);
            float target = Math.Max(minimum, Math.Min(maximum, library.ClientSize.Height * .32f));
            if (Math.Abs(library.RowStyles[2].Height - target) >= 1) library.RowStyles[2].Height = target;
        }

        private Button Action(string text, int width, EventHandler onClick, bool primary = false)
        {
            var button = new Button {
                Text = text, Width = width, Height = 34, Font = strong,
                Margin = new Padding(0, 0, 8, 0), FlatStyle = FlatStyle.Flat,
                ForeColor = primary ? Color.FromArgb(11, 39, 42) : Ink,
                BackColor = primary ? Accent : Hover, Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(163, 249, 236) : Color.FromArgb(52, 76, 91);
            button.Click += onClick;
            return button;
        }

        private void FormatLabel(Label label, Font font, Color color)
        {
            label.Font = font;
            label.ForeColor = color;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        private Label Caption(string value, Font font, Color color)
        {
            var label = new Label { Text = value };
            FormatLabel(label, font, color);
            return label;
        }

        private FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        }

        private void BuildNavigation()
        {
            navigation.BackColor = Sidebar;
            var title = Caption("FrameDeck", wordmark, Ink);
            title.SetBounds(18, 31, 195, 44);
            var sub = Caption("LOCAL VIDEO PLAYER", small, Accent);
            sub.SetBounds(22, 76, 178, 22);
            navigation.Controls.Add(title);
            navigation.Controls.Add(sub);

            libraryNav = NavButton("▦    Library", 139, 0);
            recentNav = NavButton("◷    Recent", 189, 1);
            playerNav = NavButton("▷    Now playing", 239, 2);

            var open = Action("Open video", 176, (sender, args) => ChooseVideo(), true);
            open.SetBounds(20, 315, 176, 40);
            var add = Action("Add folder", 176, (sender, args) => ChooseFolder());
            add.SetBounds(20, 364, 176, 38);
            var manage = Action("Manage folders", 176, (sender, args) => ManageFolders());
            manage.SetBounds(20, 411, 176, 38);
            navigation.Controls.Add(open);
            navigation.Controls.Add(add);
            navigation.Controls.Add(manage);
            var covers = Action("Online artwork", 176, (sender, args) => ConfigureArtwork());
            covers.SetBounds(20, 458, 176, 38);
            navigation.Controls.Add(covers);

            AddSidebarCredits();
        }

        private Button NavButton(string text, int top, int destination)
        {
            var button = Action(text, 180, (sender, args) => ShowSection(destination));
            button.SetBounds(18, top, 180, 42);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(11, 0, 0, 0);
            navigation.Controls.Add(button);
            return button;
        }

        private void BuildLibrary()
        {
            library.Dock = DockStyle.Fill;
            library.BackColor = Background;
            library.Padding = new Padding(28, 23, 28, 18);
            library.ColumnCount = 1;
            library.RowCount = 5;
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));
            library.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            library.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            body.Controls.Add(library);

            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            FormatLabel(libraryTitle, heading, Ink);
            libraryTitle.Dock = DockStyle.Fill;
            search.Font = regular;
            search.ForeColor = Ink;
            search.BackColor = Tile;
            search.BorderStyle = BorderStyle.FixedSingle;
            search.AccessibleName = "Search videos";
            search.Width = 198;
            search.TextChanged += (sender, args) => { page = 0; RenderGrid(); };
            var searchLabel = Caption("Search videos", small, Secondary);
            var searchBox = new Panel { Dock = DockStyle.Right, Width = 203 };
            searchLabel.SetBounds(2, 0, 196, 22);
            search.SetBounds(2, 22, 198, 27);
            searchBox.Controls.Add(searchLabel);
            searchBox.Controls.Add(search);
            header.Controls.Add(libraryTitle);
            header.Controls.Add(searchBox);
            library.Controls.Add(header, 0, 0);
            BuildGenreBar();

            var hero = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Tile,
                Margin = new Padding(0, 0, 0, 19)
            };
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            featuredBody = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty,
                Padding = new Padding(20, 8, 18, 8)
            };
            featuredBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            featuredBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            featuredBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            featuredBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            featuredBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
            FormatLabel(featuredTitle, heading, Ink);
            featuredTitle.Dock = DockStyle.Fill;
            featuredTitle.Margin = Padding.Empty;
            FormatLabel(featuredCaption, small, Secondary);
            featuredCaption.Dock = DockStyle.Fill;
            featuredCaption.Margin = Padding.Empty;
            featuredAction = Action("Open video", 136, (sender, args) => {
                if (featured == null) ChooseVideo();
                else Play(featured, true);
            }, true);
            featuredAction.Anchor = AnchorStyles.Left;
            featuredAction.Margin = Padding.Empty;
            featuredBody.Controls.Add(featuredTitle, 0, 0);
            featuredBody.Controls.Add(featuredCaption, 0, 1);
            FormatLabel(featuredPlot, regular, Ink);
            featuredPlot.Dock = DockStyle.Fill;
            featuredPlot.Margin = Padding.Empty;
            featuredPlot.TextAlign = ContentAlignment.TopLeft;
            featuredBody.Controls.Add(featuredPlot, 0, 2);
            var spotlightButtons = Row();
            featuredAction.Margin = new Padding(0, 0, 9, 0);
            featuredDetails = Action("Details", 88, (sender, args) => ShowMovieDetails(featuredTarget));
            spotlightButtons.Controls.AddRange(new Control[] { featuredAction, featuredDetails });
            featuredBody.Controls.Add(spotlightButtons, 0, 3);
            hero.Controls.Add(featuredBody, 0, 0);

            featuredArt = new Artwork(0, true) { Dock = DockStyle.Fill, Margin = Padding.Empty, Cursor = Cursors.Hand };
            featuredArt.Click += (sender, args) => ShowMovieDetails(featuredTarget);
            hero.Controls.Add(featuredArt, 1, 0);
            library.Controls.Add(hero, 0, 2);
            library.SizeChanged += (sender, args) => ResizeSpotlight();

            grid.Dock = DockStyle.Fill;
            grid.AutoScroll = false;
            grid.Margin = Padding.Empty;
            grid.SizeChanged += (sender, args) => ResizeLibraryGrid();
            library.Controls.Add(grid, 0, 3);

            var footer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            FormatLabel(mediaCount, small, Secondary);
            mediaCount.Dock = DockStyle.Fill;
            pager = Row();
            pager.Dock = DockStyle.Right;
            pager.Width = 191;
            previousPage = Action("‹", 42, (sender, args) => { if (page > 0) page--; RenderGrid(); });
            previousPage.AccessibleName = "Previous page";
            FormatLabel(pageText, small, Secondary);
            pageText.TextAlign = ContentAlignment.MiddleCenter;
            pageText.Size = new Size(75, 34);
            nextPage = Action("›", 42, (sender, args) => { page++; RenderGrid(); });
            nextPage.AccessibleName = "Next page";
            pager.Controls.AddRange(new Control[] { previousPage, pageText, nextPage });
            footer.Controls.Add(mediaCount);
            footer.Controls.Add(pager);
            library.Controls.Add(footer, 0, 4);
        }

        private void ShowSection(int destination)
        {
            if (destination != 2 && cleanView) SetCleanView(false);
            if (destination != 2 && miniPlayer) LeaveMiniPlayer();
            if (destination != section && destination != 2) { selectedGenre = null; spotlightSelection = null; }
            section = destination;
            libraryNav.BackColor = destination == 0 ? Hover : Sidebar;
            recentNav.BackColor = destination == 1 ? Hover : Sidebar;
            playerNav.BackColor = destination == 2 ? Hover : Sidebar;
            if (destination == 2)
            {
                player.Visible = true;
                library.Visible = false;
                player.BringToFront();
                return;
            }
            page = 0;
            libraryTitle.Text = destination == 0 ? "Video library" : "Recently played";
            player.Visible = false;
            library.Visible = true;
            library.BringToFront();
            RenderGrid();
        }

        private void RenderGrid()
        {
            if (section == 2 || nextPage == null) return;
            string query = search.Text.Trim();
            var items = VideosForSection().Where(path => MatchesGenre(path) &&
                (MovieName.FromPath(path).Label.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                DisplayName(path).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)).ToArray();
            UpdateSpotlight(PickSpotlight(items));
            RefreshGenreButtons();
            int oldOffset = page * Math.Max(1, pageSize);
            int limit = GetPageCapacity();
            if (limit != pageSize && pageSize > 0) page = oldOffset / limit;
            pageSize = limit;
            int totalPages = Math.Max(1, (items.Length + limit - 1) / limit);
            page = Math.Min(page, totalPages - 1);
            grid.SuspendLayout();
            while (grid.Controls.Count > 0) grid.Controls[0].Dispose();
            var targets = new List<ArtworkTarget>();
            foreach (string path in items.Skip(page * limit).Take(limit)) grid.Controls.Add(CreateCard(path, targets));
            if (items.Length == 0)
            {
                string message = scanning ? "Scanning folders…" : query.Length > 0 ? "No videos match your search." :
                    section == 1 ? "Videos you play will appear here." : "Add a folder to start your library.";
                var empty = Caption(message, strong, Secondary);
                empty.Size = new Size(430, 52);
                grid.Controls.Add(empty);
            }

            grid.ResumeLayout();
            mediaCount.Text = scanning ? "Scanning folders…" : String.Format("{0} {1}", items.Length, items.Length == 1 ? "video" : "videos");
            pageText.Text = String.Format("{0} / {1}", page + 1, totalPages);
            previousPage.Enabled = page > 0;
            nextPage.Enabled = page + 1 < totalPages;
            pager.Visible = totalPages > 1;
            LoadVisibleArtwork(targets);
        }

        private void ChooseVideo()
        {
            using (var dialog = new OpenFileDialog { Title = "Open a video",
                Filter = "Video files|" + String.Join(";", LibraryState.Extensions.Select(ext => "*" + ext)) + "|All files|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) Play(dialog.FileName, true);
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose a folder containing videos", ShowNewFolderButton = false })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFolders(new[] { dialog.SelectedPath });
        }

        private void AddFolders(IEnumerable<string> directories)
        {
            foreach (string directory in directories)
                if (!state.Folders.Contains(directory, StringComparer.OrdinalIgnoreCase)) state.Folders.Add(directory);
            SaveState();
            ShowSection(0);
            ScanFolders();
        }

        private void ManageFolders()
        {
            using (var dialog = new Form { Text = "Media folders", ClientSize = new Size(545, 281), MinimizeBox = false,
                MaximizeBox = false, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                BackColor = Sidebar, ForeColor = Ink })
            {
                var folders = new ListBox { Dock = DockStyle.Fill, BackColor = Tile, ForeColor = Ink, Font = regular,
                    HorizontalScrollbar = true, SelectionMode = SelectionMode.MultiExtended, AccessibleName = "Media folders" };
                folders.Items.AddRange(state.Folders.ToArray());
                var actions = Row();
                actions.Dock = DockStyle.Bottom;
                actions.Height = 49;
                actions.Padding = new Padding(10, 7, 0, 0);
                actions.Controls.Add(Action("Remove selected", 144, (sender, args) => {
                    foreach (string path in folders.SelectedItems.Cast<string>().ToArray()) folders.Items.Remove(path);
                }));
                actions.Controls.Add(Action("Apply & rescan", 138, (sender, args) => dialog.DialogResult = DialogResult.OK, true));
                actions.Controls.Add(Action("Cancel", 80, (sender, args) => dialog.DialogResult = DialogResult.Cancel));
                dialog.Controls.Add(folders);
                dialog.Controls.Add(actions);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                state.Folders = folders.Items.Cast<string>().ToList();
            }
            SaveState();
            ScanFolders();
        }

        private async void ScanFolders()
        {
            if (scan != null) scan.Cancel();
            var current = new CancellationTokenSource();
            var folders = state.Folders.ToArray();
            scan = current;
            scanning = folders.Length > 0;
            RenderGrid();
            try
            {
                string[] result = await Task.Run(() => LibraryState.Scan(folders, current.Token));
                if (scan == current && !IsDisposed && !Disposing) paths = result;
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (scan == current)
                {
                    scan = null;
                    scanning = false;
                    if (!IsDisposed && !Disposing)
                    {
                        RenderGrid();
                        IndexGenres();
                    }
                }
                current.Dispose();
            }
        }

        private void SaveState()
        {
            try { state.Save(statePath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string DisplayName(string path)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
    }
}
