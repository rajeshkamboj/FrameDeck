using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FrameDeck
{
    internal sealed class MovieDetails
    {
        public string Title { get; set; }
        public string Year { get; set; }
        public string Plot { get; set; }
        public string Rating { get; set; }
        public string RatingSource { get; set; }
        public string PosterUrl { get; set; }
        public string Source { get; set; }
        public string[] Genres { get; set; }
        public string BackdropUrl { get; set; }
        public string RatingLabel { get { return String.IsNullOrEmpty(Rating) ? "" : RatingSource + " " + Rating + "/10"; } }
    }

    internal sealed class MovieArtwork : IDisposable
    {
        public MovieDetails Movie;
        public Bitmap Image;
        public Bitmap Backdrop;

        public void Dispose()
        {
            if (Image != null) Image.Dispose();
            if (Backdrop != null) Backdrop.Dispose();
        }
    }

    internal sealed class MovieCatalog
    {
        public string TmdbToken;
        public string OmdbKey;
        private volatile string tmdbIssue;
        private volatile string omdbIssue;
        private readonly string folder;
        private readonly SemaphoreSlim workers = new SemaphoreSlim(3);
        private const int MetadataLimit = 512 * 1024;
        private const int ImageLimit = 3 * 1024 * 1024;
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);

        public MovieCatalog(string folder)
        {
            this.folder = folder;
            TmdbToken = ReadCredential("tmdb");
            OmdbKey = ReadCredential("omdb");
        }

        public string ProviderIssue { get { return omdbIssue != null ? "OMDb: " + omdbIssue : tmdbIssue != null ? "TMDB: " + tmdbIssue : null; } }
        public bool OnlineEnabled { get { return !String.IsNullOrEmpty(TmdbToken) || !String.IsNullOrEmpty(OmdbKey); } }

        public void SaveCredentials(string tmdb, string omdb)
        {
            WriteCredential("tmdb", tmdb);
            WriteCredential("omdb", omdb);
            TmdbToken = tmdb;
            OmdbKey = omdb;
            tmdbIssue = omdbIssue = null;
        }

        private string ReadCredential(string service)
        {
            try
            {
                string file = Path.Combine(folder, service + ".key");
                return File.Exists(file) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(file),
                    null, DataProtectionScope.CurrentUser)) : "";
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
            catch (CryptographicException) { return ""; }
        }

        private void WriteCredential(string service, string value)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, service + ".key");
            if (String.IsNullOrEmpty(value)) File.Delete(file);
            else File.WriteAllBytes(file, ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
        }

        public async Task<MovieArtwork> GetAsync(string path, CancellationToken token, bool spotlight = false)
        {
            await workers.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => Get(path, token, spotlight)).ConfigureAwait(false); }
            finally { workers.Release(); }
        }

        public async Task<MovieDetails> GetMetadataAsync(string path, CancellationToken token)
        {
            await workers.WaitAsync(token).ConfigureAwait(false);
            try { return await Task.Run(() => Resolve(MovieName.FromPath(path), token)).ConfigureAwait(false); }
            finally { workers.Release(); }
        }

        private MovieArtwork Get(string path, CancellationToken token, bool spotlight)
        {
            token.ThrowIfCancellationRequested();
            var name = MovieName.FromPath(path);
            MovieDetails metadata = Resolve(name, token);
            token.ThrowIfCancellationRequested();
            Bitmap poster = metadata == null ? null : LoadOnlinePoster(metadata, token);
            if (poster == null && metadata != null && metadata.Source == "OMDb" &&
                !String.IsNullOrEmpty(TmdbToken) && tmdbIssue == null)
            {
                string problem;
                MovieDetails fallback = FetchTmdb(name, TmdbToken, token, out problem);
                if (problem != null) tmdbIssue = problem;
                if (fallback != null && fallback.Year == metadata.Year && fallback.PosterUrl != metadata.PosterUrl)
                {
                    poster = LoadOnlinePoster(fallback, token);
                    if (poster != null)
                    {
                        metadata.PosterUrl = fallback.PosterUrl;
                        string cache = MetadataCacheFile(name, true, !String.IsNullOrEmpty(OmdbKey));
                        WriteCache(cache, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(metadata)));
                    }
                }
            }
            if (poster == null) poster = LocalArtwork.FindPoster(path);
            if (poster == null)
            {
                token.ThrowIfCancellationRequested();
                poster = LocalArtwork.Thumbnail(path);
            }
            var artwork = new MovieArtwork { Movie = metadata, Image = poster };
            try
            {
                if (spotlight && metadata != null && metadata.BackdropUrl == null && metadata.Source == "OMDb" &&
                    !String.IsNullOrEmpty(TmdbToken) && tmdbIssue == null)
                {
                    string problem;
                    var other = FetchTmdb(name, TmdbToken, token, out problem);
                    if (problem == null)
                    {
                        metadata.BackdropUrl = other != null && other.Year == metadata.Year ? other.BackdropUrl ?? "" : "";
                        if (other != null && other.Year == metadata.Year && (metadata.Genres == null || metadata.Genres.Length == 0))
                            metadata.Genres = other.Genres;
                        WriteCache(MetadataCacheFile(name, true, !String.IsNullOrEmpty(OmdbKey)),
                            Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(metadata)));
                    }
                }
                if (spotlight && metadata != null && !String.IsNullOrEmpty(metadata.BackdropUrl))
                    artwork.Backdrop = LoadOnlineImage(metadata.BackdropUrl, token);
                return artwork;
            }
            catch { artwork.Dispose(); throw; }
        }

        private MovieDetails Resolve(MovieName name, CancellationToken token)
        {
            // Explicitly enabling a provider sends only the cleaned title and optional year.
            string tmdb = TmdbToken;
            string omdb = OmdbKey;
            bool hasTmdb = !String.IsNullOrEmpty(tmdb);
            bool hasOmdb = !String.IsNullOrEmpty(omdb);
            if (!hasTmdb && !hasOmdb) return null;
            string cache = MetadataCacheFile(name, hasTmdb, hasOmdb);
            MovieDetails cached = null;
            try
            {
                if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < CacheLifetime)
                {
                    cached = new JavaScriptSerializer().Deserialize<MovieDetails>(File.ReadAllText(cache));
                    if (cached != null && cached.Genres != null) return cached;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            MovieDetails result = null;
            if (hasOmdb && omdbIssue == null)
            {
                string problem;
                result = FetchOmdb(name, omdb, token, out problem);
                if (problem != null) omdbIssue = problem;
            }

            Uri posterUri;
            if ((result == null || !TryPosterUri(result.PosterUrl, out posterUri) || result.Genres.Length == 0) && hasTmdb && tmdbIssue == null)
            {
                string problem;
                MovieDetails other = FetchTmdb(name, tmdb, token, out problem);
                if (problem != null) tmdbIssue = problem;
                if (result == null) result = other;
                else if (other != null && other.Year == result.Year)
                {
                    if (!TryPosterUri(result.PosterUrl, out posterUri)) result.PosterUrl = other.PosterUrl;
                    if (result.Genres.Length == 0) result.Genres = other.Genres;
                    result.BackdropUrl = other.BackdropUrl;
                }
            }

            token.ThrowIfCancellationRequested();
            if (result != null) WriteCache(cache, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(result)));
            return result ?? cached;
        }


        private MovieDetails FetchOmdb(MovieName name, string key, CancellationToken token, out string problem)
        {
            problem = null;
            string url = "https://www.omdbapi.com/?apikey=" + Uri.EscapeDataString(key) + "&type=movie&plot=full&t=" +
                Uri.EscapeDataString(name.Title) + (name.Year == null ? "" : "&y=" + name.Year);
            try
            {
                byte[] data = Request(url, null, MetadataLimit, token);
                var match = new JavaScriptSerializer().Deserialize<OmdbMovie>(Encoding.UTF8.GetString(data));
                string error = match == null ? null : match.Error;
                if (!String.IsNullOrEmpty(error))
                {
                    if (error.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0)
                        problem = "Request limit reached.";
                    else if (error.IndexOf("key", StringComparison.OrdinalIgnoreCase) >= 0)
                        problem = "Key rejected. Check the key and the activation email.";
                }
                return MatchOmdb(match, name);
            }
            catch (WebException ex) { problem = ExplainRequestFailure(ex); }
            catch (IOException) { problem = "Could not read the provider response."; }
            catch (ArgumentException) { problem = "Check the key format."; }
            catch (InvalidOperationException) { problem = "The provider response could not be read."; }
            return null;
        }

        private MovieDetails FetchTmdb(MovieName name, string credential, CancellationToken token, out string problem)
        {
            problem = null;
            string key = Regex.IsMatch(credential, @"\A[0-9a-fA-F]{32}\z") ? credential : null;
            string bearer = key == null ? credential : null;
            string url = "https://api.themoviedb.org/3/search/movie?include_adult=false&query=" + Uri.EscapeDataString(name.Title) +
                (name.Year == null ? "" : "&primary_release_year=" + name.Year) +
                (key == null ? "" : "&api_key=" + key);
            try
            {
                byte[] data = Request(url, bearer, MetadataLimit, token);
                return ParseTmdb(Encoding.UTF8.GetString(data), name);
            }
            catch (WebException ex) { problem = ExplainRequestFailure(ex); }
            catch (IOException) { problem = "Could not read the provider response."; }
            catch (ArgumentException) { problem = "Check the token format."; }
            catch (InvalidOperationException) { problem = "The provider response could not be read."; }
            return null;
        }

        internal async Task<string> TestConnections(string tmdb, string omdb, CancellationToken token)
        {
            var movie = MovieName.FromPath("Dune (2021).mkv");
            var probe = new Func<bool, string, string>((useTmdb, credential) => {
                if (String.IsNullOrEmpty(credential)) return "Key not entered.";
                string problem;
                MovieDetails result = useTmdb ? FetchTmdb(movie, credential, token, out problem) :
                    FetchOmdb(movie, credential, token, out problem);
                if (problem != null) return problem;
                if (result == null) return "Connected. Dune (2021) was not returned as an exact match.";
                using (Bitmap poster = LoadOnlinePoster(result, token, false))
                {
                    if (poster != null) return "Connected. Dune (2021) metadata and poster loaded.";
                    return String.IsNullOrEmpty(result.PosterUrl) ? "Connected. No poster was listed for Dune (2021)." :
                        "Connected. Metadata loaded, but the poster download failed.";
                }
            });
            string[] lines = await Task.WhenAll(Task.Run(() => probe(false, omdb), token),
                Task.Run(() => probe(true, tmdb), token));
            return "OMDb: " + lines[0] + Environment.NewLine + "TMDB: " + lines[1];
        }

        private static string ExplainRequestFailure(WebException exception)
        {
            using (var response = exception.Response as HttpWebResponse)
            {
                if (response != null)
                {
                    int code = (int)response.StatusCode;
                    if (code == 401 || code == 403) return "Access rejected (HTTP " + code + "). Check the key.";
                    if (code == 429) return "Request limit reached (HTTP 429).";
                    return "Provider returned HTTP " + code + ".";
                }
            }

            switch (exception.Status)
            {
                case WebExceptionStatus.TrustFailure:
                case WebExceptionStatus.SecureChannelFailure:
                    return "Could not establish a secure connection.";
                case WebExceptionStatus.Timeout:
                    return "Connection timed out.";
                case WebExceptionStatus.NameResolutionFailure:
                case WebExceptionStatus.ProxyNameResolutionFailure:
                    return "Provider address could not be resolved.";
                default:
                    return "Could not reach the provider (" + exception.Status + ").";
            }
        }


        internal static MovieDetails MatchOmdb(OmdbMovie match, MovieName name)
        {
            if (match == null || match.Response != "True" || !SameTitle(match.Title, name.Title) ||
                (name.Year != null && ReleaseYear(match.Year) != name.Year)) return null;
            return new MovieDetails { Title = match.Title, Year = ReleaseYear(match.Year), Plot = Available(match.Plot),
                Rating = Available(match.imdbRating), RatingSource = "IMDb",
                PosterUrl = Available(match.Poster), Source = "OMDb", Genres = SplitGenres(match.Genre) };
        }

        internal static MovieDetails ParseTmdb(string json, MovieName name)
        {
            var response = new JavaScriptSerializer().Deserialize<TmdbSearch>(json);
            if (response == null || response.results == null) return null;
            var matches = response.results.Where(movie =>
                (SameTitle(movie.title, name.Title) || SameTitle(movie.original_title, name.Title)) &&
                (name.Year == null || ReleaseYear(movie.release_date) == name.Year)).Take(2).ToArray();
            if (matches.Length != 1) return null;
            var match = matches[0];
            return new MovieDetails {
                Title = match.title, Year = ReleaseYear(match.release_date), Plot = match.overview,
                Rating = match.vote_count > 0 ? match.vote_average.ToString("0.0", CultureInfo.InvariantCulture) : null,
                RatingSource = "TMDB", Source = "TMDB",
                PosterUrl = TmdbImage(match.poster_path, "w342"),
                BackdropUrl = TmdbImage(match.backdrop_path, "w780"),
                Genres = (match.genre_ids ?? new int[0]).Where(GenreNames.ContainsKey).Select(id => GenreNames[id]).Distinct().ToArray()
            };
        }

        private static readonly System.Collections.Generic.Dictionary<int, string> GenreNames =
            new System.Collections.Generic.Dictionary<int, string> {
                { 28, "Action" }, { 12, "Adventure" }, { 16, "Animation" }, { 35, "Comedy" },
                { 80, "Crime" }, { 99, "Documentary" }, { 18, "Drama" }, { 10751, "Family" },
                { 14, "Fantasy" }, { 36, "History" }, { 27, "Horror" }, { 10402, "Music" },
                { 9648, "Mystery" }, { 10749, "Romance" }, { 878, "Sci-fi" }, { 10770, "TV Movie" },
                { 53, "Thriller" }, { 10752, "War" }, { 37, "Western" }
            };

        private static string[] SplitGenres(string value)
        {
            return (Available(value) ?? "").Split(',').Select(genre => genre.Trim())
                .Where(genre => genre.Length > 0)
                .Select(genre => genre.Equals("Sci-Fi", StringComparison.OrdinalIgnoreCase) ||
                    genre.Equals("Science Fiction", StringComparison.OrdinalIgnoreCase) ? "Sci-fi" : genre)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string TmdbImage(string path, string size)
        {
            return path != null && Regex.IsMatch(path, @"^/[A-Za-z0-9_.-]+\.(jpg|png)$")
                ? "https://image.tmdb.org/t/p/" + size + path : null;
        }

        private static string ReleaseYear(string value)
        {
            return !String.IsNullOrEmpty(value) && Regex.IsMatch(value, @"^\d{4}") ? value.Substring(0, 4) : null;
        }

        private static bool SameTitle(string left, string right)
        {
            if (String.IsNullOrWhiteSpace(left) || String.IsNullOrWhiteSpace(right)) return false;
            return String.Equals(NormalizeTitle(left), NormalizeTitle(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string title)
        {
            return new string(title.Normalize(NormalizationForm.FormD).Where(Char.IsLetterOrDigit).ToArray());
        }

        private static string Available(string value)
        {
            return String.IsNullOrWhiteSpace(value) || value == "N/A" ? null : value;
        }

        private static bool TryPosterUri(string value, out Uri uri)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || !uri.IsDefaultPort ||
                !String.IsNullOrEmpty(uri.UserInfo) || (uri.Host != "image.tmdb.org" && uri.Host != "m.media-amazon.com" &&
                uri.Host != "ia.media-imdb.com" && uri.Host != "images-na.ssl-images-amazon.com")) return false;
            if (uri.Scheme == "http") uri = new UriBuilder(uri) { Scheme = "https", Port = -1 }.Uri;
            return uri.Scheme == "https";
        }

        private Bitmap LoadOnlinePoster(MovieDetails movie, CancellationToken token, bool allowCache = true)
        {
            return LoadOnlineImage(movie.PosterUrl, token, allowCache);
        }

        private Bitmap LoadOnlineImage(string url, CancellationToken token, bool allowCache = true)
        {
            Uri uri;
            if (!TryPosterUri(url, out uri)) return null;
            string cache = CacheFile(uri.AbsoluteUri, ".poster");
            if (allowCache)
            {
                Bitmap existing = LocalArtwork.Load(cache);
                if (existing != null) return existing;
            }
            try
            {
                byte[] bytes = Request(uri.AbsoluteUri, null, ImageLimit, token, true);
                using (var stream = new MemoryStream(bytes))
                {
                    Bitmap image = LocalArtwork.Decode(stream);
                    if (image != null && allowCache) WriteCache(cache, bytes);
                    return image;
                }
            }
            catch (WebException ex)
            {
                if (ex.Response != null) ex.Response.Dispose();
                return null;
            }
            catch (IOException) { return null; }
        }


        private string MetadataCacheFile(MovieName name, bool hasTmdb, bool hasOmdb)
        {
            return CacheFile(name.Label + ":" + hasTmdb + ":" + hasOmdb, ".json");
        }

        private string CacheFile(string key, string extension)
        {
            using (var sha = SHA256.Create())
                return Path.Combine(folder, BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-", "") + extension);
        }

        private static void WriteCache(string path, byte[] data)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static byte[] Request(string url, string bearer, int limit, CancellationToken token, bool posterRedirect = false)
        {
            token.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = false;
            request.Timeout = 6000;
            request.ReadWriteTimeout = 6000;
            if (bearer != null) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            try
            {
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var input = response.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    int status = (int)response.StatusCode;
                    if (posterRedirect && status >= 300 && status < 400)
                    {
                        Uri next, verified;
                        if (Uri.TryCreate(new Uri(url), response.Headers[HttpResponseHeader.Location], out next) &&
                            TryPosterUri(next.AbsoluteUri, out verified))
                            return Request(verified.AbsoluteUri, null, limit, token);
                    }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit)
                        throw new IOException("Unexpected response.");
                    byte[] buffer = new byte[8192];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (output.Length + count > limit) throw new IOException("Response too large.");
                        output.Write(buffer, 0, count);
                    }
                    return output.ToArray();
                }
            }
            catch (WebException) { token.ThrowIfCancellationRequested(); throw; }
        }

        public sealed class TmdbSearch { public TmdbMovie[] results { get; set; } }
        public sealed class TmdbMovie
        {
            public string title { get; set; }
            public string original_title { get; set; }
            public string release_date { get; set; }
            public string overview { get; set; }
            public string poster_path { get; set; }
            public string backdrop_path { get; set; }
            public int[] genre_ids { get; set; }
            public double vote_average { get; set; }
            public int vote_count { get; set; }
        }
        public sealed class OmdbMovie
        {
            public string Response { get; set; }
            public string Error { get; set; }
            public string Title { get; set; }
            public string Year { get; set; }
            public string Plot { get; set; }
            public string Poster { get; set; }
            public string imdbRating { get; set; }
            public string Genre { get; set; }
        }
    }
}
