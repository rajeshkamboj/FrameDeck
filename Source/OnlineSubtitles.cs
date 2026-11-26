using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace FrameDeck
{
    internal sealed class OnlineSubtitles
    {
        private static readonly Uri DownloadHost = new Uri("https://dl.subdl.com/");
        private static readonly Regex DownloadPath = new Regex(@"\A/subtitle/[A-Za-z0-9_-]+(?:/[A-Za-z0-9_-]+|\.zip)\z");
        private static readonly Regex ReleaseTags = new Regex(@"\b(2160p|1080p|720p|480p|web[-. ]?(?:rip|dl)|blu[-. ]?ray|bdrip|remux|hdtv)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private const int SubtitleLimit = 2 * 1024 * 1024;
        private const int ArchiveLimit = 6 * 1024 * 1024;
        private readonly string folder;

        internal sealed class Result
        {
            public string Release { get; set; }
            public string Language { get; set; }
            public bool HearingImpaired { get; set; }
            public string Path { get; set; }
            public string Extension { get; set; }
            public override string ToString() { return Release + (HearingImpaired ? "  [SDH]" : ""); }
        }

        public OnlineSubtitles(string folder) { this.folder = folder; }

        public string ReadKey()
        {
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(folder, "subdl.key")),
                    null, DataProtectionScope.CurrentUser));
            }
            catch (IOException) { return ""; }
            catch (UnauthorizedAccessException) { return ""; }
            catch (CryptographicException) { return ""; }
        }

        public void SaveKey(string key)
        {
            Directory.CreateDirectory(folder);
            WriteAtomic(Path.Combine(folder, "subdl.key"), ProtectedData.Protect(Encoding.UTF8.GetBytes(key),
                null, DataProtectionScope.CurrentUser));
        }

        public Result[] Search(string key, string title, string year, string language, string localName, CancellationToken token)
        {
            string url = "https://api.subdl.com/api/v1/subtitles?type=movie&client=custom_integration&unpack=1&subs_per_page=20" +
                "&api_key=" + Uri.EscapeDataString(key) + "&film_name=" + Uri.EscapeDataString(title) +
                "&languages=" + Uri.EscapeDataString(language) + (year.Length == 0 ? "" : "&year=" + year);
            return ParseSearch(Request(new Uri(url), SubtitleLimit, token, "application/json"), title, year, language, localName);
        }

        internal static Result[] ParseSearch(byte[] response, string title, string year, string language, string localName)
        {
            SearchResponse data;
            try { data = new JavaScriptSerializer().Deserialize<SearchResponse>(Encoding.UTF8.GetString(response)); }
            catch (ArgumentException) { throw new InvalidDataException("SubDL returned an unreadable search response."); }
            catch (InvalidOperationException) { throw new InvalidDataException("SubDL returned an unreadable search response."); }

            if (data == null || !data.status)
            {
                string error = data == null ? "" : data.error ?? "";
                if (error.IndexOf("api key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException("SubDL rejected the API key. Check it in your SubDL account.");
                if (error.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidDataException("The SubDL request limit was reached. Try again later.");
                if (error.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("no subtitle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    error.IndexOf("no result", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new Result[0];
                throw new InvalidDataException("SubDL could not complete this search. Check the title or try again.");
            }

            if (data.results == null || data.results.Length == 0) return new Result[0];
            string expectedTitle = Normalize(title);
            var first = data.results[0];
            if (first == null || Normalize(first.name) != expectedTitle ||
                (!String.IsNullOrEmpty(first.type) && !first.type.Equals("movie", StringComparison.OrdinalIgnoreCase)) ||
                (year.Length > 0 && first.year.ToString(CultureInfo.InvariantCulture) != year))
                return new Result[0];

            if (year.Length == 0 && data.results.Any(movie => movie != null && Normalize(movie.name) == expectedTitle &&
                movie.year != first.year))
                throw new InvalidDataException("SubDL found multiple movies with this title. Enter the release year and search again.");

            var results = new List<Result>();
            foreach (var item in data.subtitles ?? new SubtitleRecord[0])
            {
                if (item == null) continue;
                bool unpacked = false;
                foreach (var file in item.unpack_files ?? new SubtitleFile[0])
                {
                    if (file == null || !MatchesLanguage(file.language, language) || !IsDownloadPath(file.url)) continue;
                    string extension = Extension(file.format, file.name);
                    if (extension == null) continue;
                    results.Add(new Result { Release = Title(file.release_name, item.release_name, file.name),
                        Language = language, HearingImpaired = file.hi || item.hi, Path = file.url, Extension = extension });
                    unpacked = true;
                }

                if (!unpacked && MatchesLanguage(item.lang, language) && IsDownloadPath(item.url) &&
                    item.url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    results.Add(new Result { Release = Title(item.release_name, item.name, null),
                        Language = language, HearingImpaired = item.hi, Path = item.url });
            }

            return results.GroupBy(result => result.Path, StringComparer.Ordinal).Select(group => group.First())
                .OrderByDescending(result => ReleaseScore(localName, result.Release))
                .ThenBy(result => result.HearingImpaired).Take(30).ToArray();
        }

        private static bool MatchesLanguage(string code, string requested)
        {
            if (String.IsNullOrWhiteSpace(code)) return true; // The server already filters by the requested language.
            if (code.Equals(requested, StringComparison.OrdinalIgnoreCase)) return true;
            if (code.Length <= 2) return false;
            // Legacy pack records can return an English display name instead of a code.
            try
            {
                return code.Equals(CultureInfo.GetCultureInfo(requested.ToLowerInvariant()).EnglishName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (CultureNotFoundException) { return false; }
        }

        private static int ReleaseScore(string localName, string remoteName)
        {
            string cleanedLocal = Normalize(Path.GetFileNameWithoutExtension(localName));
            string cleanedRemote = Normalize(remoteName);
            if (cleanedLocal.Length > 0 && cleanedRemote.Contains(cleanedLocal)) return 100;
            var local = ReleaseTags.Matches(localName.Replace('_', ' ')).Cast<Match>().Select(match => Normalize(match.Value));
            var remote = ReleaseTags.Matches(remoteName.Replace('_', ' ')).Cast<Match>().Select(match => Normalize(match.Value));
            return local.Intersect(remote).Count();
        }

        private static string Normalize(string name)
        {
            return new string((name ?? "").Normalize(NormalizationForm.FormD).Where(Char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static string Title(string release, string alternate, string filename)
        {
            return new[] { release, alternate, filename }.FirstOrDefault(s => !String.IsNullOrWhiteSpace(s)) ?? "Subtitle";
        }

        private static bool IsDownloadPath(string path)
        {
            return path != null && DownloadPath.IsMatch(path);
        }

        private static string Extension(string format, string name)
        {
            string extension = "." + (format ?? "").TrimStart('.').ToLowerInvariant();
            if (extension == ".") extension = (Path.GetExtension(name ?? "") ?? "").ToLowerInvariant();
            return extension == ".srt" || extension == ".ass" || extension == ".ssa" || extension == ".vtt" ? extension : null;
        }

        public string Download(Result result, string videoPath, CancellationToken token)
        {
            if (!IsDownloadPath(result.Path)) throw new InvalidDataException("SubDL returned an invalid download link.");
            byte[] response = Request(new Uri(DownloadHost, result.Path), ArchiveLimit, token, null);
            string extension;
            byte[] subtitle = Unpack(response, result.Extension, result.Release, token, out extension);
            string destination = Path.Combine(MovieFolder(videoPath), "SubDL-" + result.Language + "-" + Hash(result.Path).Substring(0, 16) + extension);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            WriteAtomic(destination, subtitle);
            return destination;
        }

        internal static byte[] Unpack(byte[] bytes, string suggestedExtension, string release, CancellationToken token, out string extension)
        {
            if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
            {
                using (var input = new MemoryStream(bytes))
                using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
                {
                    if (zip.Entries.Count > 150) throw new InvalidDataException("The subtitle archive contains too many files.");
                    var entry = zip.Entries.Where(item => !String.IsNullOrEmpty(item.Name) &&
                        Extension(null, item.Name) != null && item.Length > 0 && item.Length <= SubtitleLimit)
                        .OrderByDescending(item => ReleaseScore(item.Name, release))
                        .ThenBy(item => Extension(null, item.Name) == ".srt" ? 0 : 1).FirstOrDefault();
                    if (entry == null) throw new InvalidDataException("The download did not contain a supported subtitle.");
                    extension = Extension(null, entry.Name);
                    using (var file = entry.Open()) bytes = ReadLimited(file, SubtitleLimit, token);
                }
            }
            else extension = Extension(suggestedExtension, null);
            if (extension == null || bytes.Length == 0 || bytes.Length > SubtitleLimit)
                throw new InvalidDataException("The download did not contain a supported subtitle.");

            string text;
            using (var stream = new MemoryStream(bytes))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true)) text = reader.ReadToEnd();
            bool isSubtitle = extension == ".srt" ? Regex.IsMatch(text, @"(?m)^\s*\d{1,3}:\d{2}:\d{2}[,.]\d{3}\s+-->\s+\d") :
                extension == ".vtt" ? text.TrimStart().StartsWith("WEBVTT", StringComparison.Ordinal) :
                text.IndexOf("[Events]", StringComparison.OrdinalIgnoreCase) >= 0 &&
                text.IndexOf("Dialogue:", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isSubtitle || text.IndexOf('\0') >= 0)
                throw new InvalidDataException("The download was not a readable subtitle file.");
            return bytes;
        }

        public IEnumerable<string> CachedFor(string videoPath)
        {
            string directory = MovieFolder(videoPath);
            if (!Directory.Exists(directory)) return new string[0];
            try { return Directory.GetFiles(directory, "SubDL-*").Where(path => Extension(null, path) != null)
                .OrderByDescending(File.GetLastWriteTimeUtc).ToArray(); }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        private string MovieFolder(string path)
        {
            return Path.Combine(folder, Hash(Path.GetFullPath(path).ToUpperInvariant()));
        }

        private static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        private static void WriteAtomic(string path, byte[] data)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, data);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static byte[] Request(Uri uri, int limit, CancellationToken token, string accept)
        {
            token.ThrowIfCancellationRequested();
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Accept = accept ?? "*/*";
            request.UserAgent = "FrameDeck";
            try
            {
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var stream = response.GetResponseStream())
                {
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit)
                        throw new InvalidDataException("SubDL returned an unexpected response.");
                    return ReadLimited(stream, limit, token);
                }
            }
            catch (WebException error)
            {
                token.ThrowIfCancellationRequested();
                using (var response = error.Response as HttpWebResponse)
                {
                    int status = response == null ? 0 : (int)response.StatusCode;
                    if (status == 401 || status == 403) throw new InvalidDataException("SubDL denied access. Check the API key and account.");
                    if (status == 429) throw new InvalidDataException("The SubDL request limit was reached. Try again later.");
                }
                throw new IOException("Could not reach SubDL. Check the connection and try again.");
            }
        }

        private static byte[] ReadLimited(Stream stream, int limit, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (output.Length + read > limit) throw new InvalidDataException("The subtitle download is too large.");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        public sealed class SearchResponse
        {
            public bool status { get; set; }
            public string error { get; set; }
            public MovieMatch[] results { get; set; }
            public SubtitleRecord[] subtitles { get; set; }
        }

        public sealed class MovieMatch
        {
            public string name { get; set; }
            public string type { get; set; }
            public int year { get; set; }
        }

        public sealed class SubtitleRecord
        {
            public string release_name { get; set; }
            public string name { get; set; }
            public string url { get; set; }
            public string lang { get; set; }
            public bool hi { get; set; }
            public SubtitleFile[] unpack_files { get; set; }
        }

        public sealed class SubtitleFile
        {
            public string release_name { get; set; }
            public string name { get; set; }
            public string url { get; set; }
            public string language { get; set; }
            public string format { get; set; }
            public bool hi { get; set; }
        }
    }
}
