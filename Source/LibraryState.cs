using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;

namespace FrameDeck
{
    public sealed class LibraryState
    {
        internal static readonly string[] Extensions = {
            ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".mpeg", ".mpg",
            ".wmv", ".flv", ".ts", ".m2ts", ".mts", ".vob", ".3gp", ".ogv", ".asf"
        };

        public List<string> Folders { get; set; }
        public List<string> Recent { get; set; }
        public Dictionary<string, long> Positions { get; set; }
        public int Volume { get; set; }

        public LibraryState()
        {
            Folders = new List<string>();
            Recent = new List<string>();
            Positions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Volume = 80;
        }

        public static LibraryState Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new LibraryState();
                var state = new JavaScriptSerializer().Deserialize<LibraryState>(File.ReadAllText(path));
                state.Positions = new Dictionary<string, long>(state.Positions, StringComparer.OrdinalIgnoreCase);
                state.Volume = Math.Min(100, Math.Max(0, state.Volume));
                return state;
            }
            catch (IOException) { return new LibraryState(); }
            catch (UnauthorizedAccessException) { return new LibraryState(); }
            catch (ArgumentException) { return new LibraryState(); }
            catch (InvalidOperationException) { return new LibraryState(); }
            catch (NullReferenceException) { return new LibraryState(); }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Record(string path)
        {
            Recent.RemoveAll(item => StringComparer.OrdinalIgnoreCase.Equals(item, path));
            Recent.Insert(0, path);
            while (Recent.Count > 100)
            {
                Positions.Remove(Recent[Recent.Count - 1]);
                Recent.RemoveAt(Recent.Count - 1);
            }
        }

        internal static string[] Scan(string[] folders, CancellationToken token)
        {
            var videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>(folders);
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                if (!seen.Add(directory)) continue;
                try
                {
                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                            videos.Add(file);
                    }
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return videos.OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
