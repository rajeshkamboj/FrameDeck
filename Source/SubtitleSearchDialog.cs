using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FrameDeck
{
    internal sealed partial class DeckWindow
    {
        private OnlineSubtitles SubtitleProvider()
        {
            return new OnlineSubtitles(Path.Combine(Path.GetDirectoryName(statePath), "subtitles"));
        }

        private void FindOnlineSubtitles(int generation)
        {
            if (!CanUseTrackMenu(generation) || activeFile == null) return;
            string moviePath = activeFile;
            var movie = MovieName.FromPath(moviePath);
            var provider = SubtitleProvider();
            string selectedPath = null;
            using (var dialog = NewArtworkDialog("Find online subtitles", 640, 461))
            using (var lifetime = new CancellationTokenSource())
            {
                var token = lifetime.Token;
                var instructions = Caption("Search SubDL by movie title, year and language. Pick a release that matches your video.", regular, Ink);
                instructions.SetBounds(18, 12, 602, 42);
                dialog.Controls.Add(instructions);
                var title = SubtitleInput(dialog, "Movie title", movie.Title, 18, 55, 399);
                title.MaxLength = 180;
                var year = SubtitleInput(dialog, "Year", movie.Year ?? "", 426, 55, 60);
                year.MaxLength = 4;
                var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Subtitle language" };
                var languageNames = new[] { "English", "Hindi", "Spanish", "French", "German", "Arabic", "Portuguese", "Italian", "Turkish" };
                var languageCodes = new[] { "EN", "HI", "ES", "FR", "DE", "AR", "PT", "IT", "TR" };
                language.Items.AddRange(languageNames);
                language.SelectedIndex = 0;
                language.SetBounds(497, 80, 123, 28);
                var languageLabel = Caption("Language", small, Secondary);
                languageLabel.SetBounds(497, 55, 123, 24);
                dialog.Controls.AddRange(new Control[] { languageLabel, language });

                var key = SubtitleInput(dialog, "SubDL API key", provider.ReadKey(), 18, 121, 485);
                key.UseSystemPasswordChar = true;
                key.MaxLength = 200;
                var getKey = Action("Get key", 104, (sender, args) =>
                    Process.Start(new ProcessStartInfo("https://subdl.com/developers#faq") { UseShellExecute = true }));
                getKey.Location = new Point(516, 142);
                dialog.Controls.Add(getKey);

                var results = new ListBox {
                    BackColor = Tile, ForeColor = Ink, Font = regular,
                    HorizontalScrollbar = true, AccessibleName = "Matching subtitle releases"
                };
                results.SetBounds(18, 187, 602, 157);
                dialog.Controls.Add(results);
                var status = Caption("The search sends the title, year, language and key to SubDL.", small, Secondary);
                status.SetBounds(18, 350, 602, 48);
                dialog.Controls.Add(status);

                bool busy = false;
                Button searchButton = null, download = null;
                Action refresh = () => {
                    searchButton.Enabled = !busy;
                    download.Enabled = !busy && results.SelectedItem is OnlineSubtitles.Result;
                };
                searchButton = Action("Search", 101, async (sender, args) => {
                    if (busy) return;
                    string value = key.Text.Trim(), movieTitle = title.Text.Trim(), movieYear = year.Text.Trim();
                    if (value.Length == 0) { status.Text = "Enter your SubDL API key first."; key.Focus(); return; }
                    if (movieTitle.Length == 0) { status.Text = "Enter a movie title."; title.Focus(); return; }
                    if (movieYear.Length != 0 && !Regex.IsMatch(movieYear, @"\A(?:18|19|20)\d{2}\z"))
                    { status.Text = "Enter a four-digit release year, or leave it blank."; year.Focus(); return; }
                    string code = languageCodes[language.SelectedIndex];
                    busy = true;
                    results.Items.Clear();
                    status.Text = "Searching SubDL...";
                    refresh();
                    try
                    {
                        var matches = await Task.Run(() => {
                            var found = provider.Search(value, movieTitle, movieYear, code, Path.GetFileName(moviePath), token);
                            token.ThrowIfCancellationRequested();
                            provider.SaveKey(value);
                            return found;
                        }, token);
                        if (dialog.IsDisposed || token.IsCancellationRequested) return;
                        results.Items.AddRange(matches);
                        if (matches.Length > 0) results.SelectedIndex = 0;
                        status.Text = matches.Length == 0 ? "No matching subtitles. Try the original title, release year, or another language." :
                            matches.Length + (matches.Length == 1 ? " result" : " results") + ". SDH includes sound cues for deaf or hard-of-hearing viewers.";
                    }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
                    catch (InvalidDataException ex) { if (!dialog.IsDisposed && !token.IsCancellationRequested) status.Text = ex.Message; }
                    catch (IOException ex) { if (!dialog.IsDisposed && !token.IsCancellationRequested) status.Text = ex.Message; }
                    catch (UnauthorizedAccessException) { if (!dialog.IsDisposed) status.Text = "FrameDeck could not save the API key."; }
                    catch (CryptographicException) { if (!dialog.IsDisposed) status.Text = "Windows could not protect the API key."; }
                    finally { busy = false; if (!dialog.IsDisposed && !token.IsCancellationRequested) refresh(); }
                });
                download = Action("Download and load", 155, async (sender, args) => {
                    var match = results.SelectedItem as OnlineSubtitles.Result;
                    if (busy || match == null) return;
                    busy = true;
                    refresh();
                    status.Text = "Downloading the selected subtitle...";
                    try
                    {
                        string path = await Task.Run(() => provider.Download(match, moviePath, token), token);
                        if (dialog.IsDisposed || token.IsCancellationRequested) return;
                        selectedPath = path;
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (OperationCanceledException) { }
                    catch (ObjectDisposedException) { if (!token.IsCancellationRequested) throw; }
                    catch (InvalidDataException ex) { if (!dialog.IsDisposed && !token.IsCancellationRequested) status.Text = ex.Message; }
                    catch (IOException ex) { if (!dialog.IsDisposed && !token.IsCancellationRequested) status.Text = ex.Message; }
                    catch (UnauthorizedAccessException) { if (!dialog.IsDisposed) status.Text = "FrameDeck could not save the subtitle."; }
                    finally { busy = false; if (!dialog.IsDisposed && !token.IsCancellationRequested) refresh(); }
                }, true);
                var close = Action("Close", 88, (sender, args) => dialog.DialogResult = DialogResult.Cancel);
                searchButton.Location = new Point(18, 405);
                download.Location = new Point(127, 405);
                close.Location = new Point(291, 405);
                dialog.Controls.AddRange(new Control[] { searchButton, download, close });
                results.SelectedIndexChanged += (sender, args) => refresh();
                dialog.AcceptButton = searchButton;
                dialog.CancelButton = close;
                refresh();
                dialog.ShowDialog(this);
                lifetime.Cancel();
            }
            if (selectedPath != null && CanUseTrackMenu(generation))
            {
                matchingSubtitles = SubtitleFiles.Find(moviePath).Concat(SubtitleProvider().CachedFor(moviePath))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                LoadSubtitle(selectedPath);
            }
        }

        private TextBox SubtitleInput(Form dialog, string text, string value, int x, int y, int width)
        {
            var label = Caption(text, small, Secondary);
            label.SetBounds(x, y, width, 24);
            var box = new TextBox { Text = value, AccessibleName = text, Font = regular };
            box.SetBounds(x, y + 25, width, 28);
            dialog.Controls.Add(label);
            dialog.Controls.Add(box);
            return box;
        }
    }
}
