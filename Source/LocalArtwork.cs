using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FrameDeck
{
    internal sealed class MovieName
    {
        private static readonly Regex Release = new Regex(
            @"(?:^|[\s._-])(?:2160p|1080[pi]|720p|480p|4k|web[- .]?(?:dl|rip)|blu[- .]?ray|b[dr]rip|bdr|hdtv|dvdrip|x26[45]|h[ .]?26[45]|hevc|av1|aac[0-9.]*)(?=$|[\s._()\[\]-])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex YearPattern = new Regex(@"(?:\s+|\s*\()\b((?:19|20)\d{2})\)?\s*$");

        public string Title { get; private set; }
        public string Year { get; private set; }
        public string Label { get { return Year == null ? Title : Title + " (" + Year + ")"; } }

        public static MovieName FromPath(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string name = Release.Split(file, 2)[0];
            name = Regex.Replace(name, @"[._]+", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '_', '.');
            if (name.Length == 0) name = file;

            var yearMatch = YearPattern.Match(name);
            if (yearMatch.Success && yearMatch.Index > 0 && Int32.Parse(yearMatch.Groups[1].Value) <= DateTime.UtcNow.Year + 2)
                return new MovieName { Title = name.Substring(0, yearMatch.Index).Trim(), Year = yearMatch.Groups[1].Value };
            return new MovieName { Title = name };
        }
    }

    internal static class LocalArtwork
    {
        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public static Bitmap FindPoster(string videoPath)
        {
            string directory = Path.GetDirectoryName(videoPath);
            string basename = Path.Combine(directory, Path.GetFileNameWithoutExtension(videoPath));
            foreach (string stem in new[] { basename, basename + "-poster", basename + "-cover" })
            {
                Bitmap image = Find(stem);
                if (image != null) return image;
            }

            // Apply a generic poster only when the folder contains a single video.
            try
            {
                if (Directory.EnumerateFiles(directory).Where(path => LibraryState.Extensions.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).Take(2).Count() != 1) return null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            foreach (string name in new[] { "poster", "cover", "folder" })
            {
                Bitmap image = Find(Path.Combine(directory, name));
                if (image != null) return image;
            }
            return null;
        }

        private static Bitmap Find(string stem)
        {
            foreach (string extension in ImageExtensions)
            {
                var image = Load(stem + extension);
                if (image != null) return image;
            }
            return null;
        }

        public static Bitmap Load(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using (Stream stream = File.OpenRead(path)) return Decode(stream);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static Bitmap Decode(Stream stream)
        {
            try
            {
                using (Image image = Image.FromStream(stream))
                {
                    float scale = Math.Min(1f, 520f / Math.Max(image.Width, image.Height));
                    var resized = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                    using (var graphics = Graphics.FromImage(resized))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, 0, 0, resized.Width, resized.Height);
                    }
                    return resized;
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; } // GDI+ reports invalid image data this way.
        }

        public static Bitmap Thumbnail(string path)
        {
            ShellImageFactory factory;
            Guid id = typeof(ShellImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out factory) < 0 || factory == null) return null;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                // SIIGBF_THUMBNAILONLY prevents a file-type icon from being used as cover art.
                return factory.GetImage(new NativeSize { Width = 400, Height = 280 }, 8, out bitmap) >= 0 &&
                    bitmap != IntPtr.Zero ? Image.FromHbitmap(bitmap) : null;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                Marshal.ReleaseComObject(factory);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize { public int Width; public int Height; }

        [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ShellImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid id,
            [MarshalAs(UnmanagedType.Interface)] out ShellImageFactory factory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
