using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrameDeck
{
    internal static class SubtitleFiles
    {
        private static readonly string[] Extensions = {
            ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".smi", ".sami", ".ttml"
        };

        public const string DialogFilter =
            "Subtitle files|*.srt;*.ass;*.ssa;*.vtt;*.sub;*.idx;*.smi;*.sami;*.ttml|All files|*.*";

        public static string[] Find(string video)
        {
            string directory = Path.GetDirectoryName(video);
            string title = Path.GetFileNameWithoutExtension(video);
            var matches = MatchingFiles(directory, title).ToList();
            foreach (string name in new[] { "Subs", "Subtitles", "Captions" })
            {
                string folder = Path.Combine(directory, name);
                matches.AddRange(MatchingFiles(folder, title));
                // A movie-specific subfolder can contain files named only by language.
                matches.AddRange(MatchingFiles(Path.Combine(folder, title), null));
            }

            return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static IEnumerable<string> MatchingFiles(string folder, string title)
        {
            if (!Directory.Exists(folder)) return new string[0];
            try
            {
                var files = Directory.GetFiles(folder);
                var names = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                return files.Where(file => {
                    string extension = Path.GetExtension(file);
                    if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
                    if (extension.Equals(".sub", StringComparison.OrdinalIgnoreCase) &&
                        names.Contains(Path.ChangeExtension(file, ".idx"))) return false;
                    string name = Path.GetFileNameWithoutExtension(file);
                    return title == null || name.Equals(title, StringComparison.OrdinalIgnoreCase) ||
                        (name.Length > title.Length && name.StartsWith(title, StringComparison.OrdinalIgnoreCase) &&
                            ". _-[(".IndexOf(name[title.Length]) >= 0);
                }).OrderBy(file => !String.Equals(Path.GetFileNameWithoutExtension(file), title, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }
    }
}
