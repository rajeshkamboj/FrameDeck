using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FrameDeck
{
    internal sealed class VlcEngine : IDisposable
    {
        internal sealed class Track
        {
            public int Id;
            public string Name;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrackNode
        {
            public int Id;
            public IntPtr Name;
            public IntPtr Next;
        }

        private IntPtr instance;
        private IntPtr player;
        private const string Library = "libvlc.dll";
        public string Version { get; private set; }
        public int State { get { return libvlc_media_player_get_state(player); } }
        public long Time { get { return libvlc_media_player_get_time(player); } }
        public long Length { get { return libvlc_media_player_get_length(player); } }
        public bool IsSeekable { get { return libvlc_media_player_is_seekable(player) != 0; } }

        public VlcEngine(IntPtr surface)
        {
            var locations = new[] {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vlc"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"VideoLAN\VLC"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\VideoLAN\VLC")
            };
            string directory = Array.Find(locations, path =>
                File.Exists(Path.Combine(path, Library)) && Directory.Exists(Path.Combine(path, "plugins")));
            if (directory == null)
                throw new InvalidOperationException("Install 64-bit VLC 3 from videolan.org, then reopen FrameDeck.");

            string pathToDll = Path.Combine(directory, Library);
            if (LoadLibraryEx(pathToDll, IntPtr.Zero, 0x00000008) == IntPtr.Zero)
                throw new InvalidOperationException("FrameDeck could not load the VLC engine. Check that VLC 3 is 64-bit.");

            Version = ReadUtf8(libvlc_get_version());
            if (!Version.StartsWith("3.", StringComparison.Ordinal))
                throw new InvalidOperationException("FrameDeck requires the 64-bit VLC 3 engine.");
            instance = libvlc_new(0, IntPtr.Zero);
            if (instance == IntPtr.Zero)
                throw new InvalidOperationException("The VLC engine could not start. Check that your VLC installation includes its plugins.");
            player = libvlc_media_player_new(instance);
            if (player == IntPtr.Zero)
            {
                Dispose();
                throw new InvalidOperationException("The VLC playback session could not start.");
            }

            libvlc_media_player_set_hwnd(player, surface);
            libvlc_video_set_key_input(player, 0);
            libvlc_video_set_mouse_input(player, 0);
        }

        public bool Play(string path)
        {
            var media = libvlc_media_new_path(instance, Utf8(path));
            if (media == IntPtr.Zero) return false;
            try
            {
                // FrameDeck supplies matching files explicitly so VLC cannot pick an unrelated sidecar.
                libvlc_media_add_option(media, Utf8(":no-sub-autodetect-file"));
                libvlc_media_player_set_media(player, media);
                return libvlc_media_player_play(player) == 0;
            }
            finally { libvlc_media_release(media); }
        }

        public void TogglePause() { libvlc_media_player_set_pause(player, State == 4 ? 0 : 1); }
        public void Stop() { libvlc_media_player_stop(player); }
        public void SetVolume(int value) { libvlc_audio_set_volume(player, value); }
        public void SetMute(bool value) { libvlc_audio_set_mute(player, value ? 1 : 0); }
        public void Seek(long milliseconds) { libvlc_media_player_set_time(player, milliseconds); }
        public int VideoOutputs { get { return (int)libvlc_media_player_has_vout(player); } }
        public void SetAspectRatio(string ratio)
        {
            libvlc_video_set_aspect_ratio(player, ratio == null ? null : Utf8(ratio));
        }

        public bool AddSubtitle(string path)
        {
            return libvlc_media_player_add_slave(player, 0, Utf8(new Uri(path).AbsoluteUri), true) == 0;
        }

        public List<Track> GetTracks(bool subtitles)
        {
            var result = new List<Track>();
            IntPtr head = subtitles ? libvlc_video_get_spu_description(player) :
                libvlc_audio_get_track_description(player);
            try
            {
                for (var node = head; node != IntPtr.Zero;)
                {
                    var description = (TrackNode)Marshal.PtrToStructure(node, typeof(TrackNode));
                    result.Add(new Track { Id = description.Id, Name = ReadUtf8(description.Name) });
                    node = description.Next;
                }
            }
            finally
            {
                if (head != IntPtr.Zero) libvlc_track_description_list_release(head);
            }
            return result;
        }

        public int CurrentTrack(bool subtitles)
        {
            return subtitles ? libvlc_video_get_spu(player) : libvlc_audio_get_track(player);
        }

        public bool SelectTrack(bool subtitles, int id)
        {
            return (subtitles ? libvlc_video_set_spu(player, id) : libvlc_audio_set_track(player, id)) == 0;
        }

        private static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }

        private static string ReadUtf8(IntPtr value)
        {
            if (value == IntPtr.Zero) return "";
            int length = 0;
            while (Marshal.ReadByte(value, length) != 0) length++;
            var bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        public void Dispose()
        {
            if (player != IntPtr.Zero)
            {
                libvlc_media_player_stop(player);
                libvlc_media_player_release(player);
                player = IntPtr.Zero;
            }
            if (instance != IntPtr.Zero) { libvlc_release(instance); instance = IntPtr.Zero; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_get_version();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_new(int argc, IntPtr argv);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_release(IntPtr core);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_media_player_new(IntPtr core);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_release(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_set_hwnd(IntPtr player, IntPtr window);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_video_set_key_input(IntPtr player, uint enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_video_set_mouse_input(IntPtr player, uint enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_media_new_path(IntPtr core, byte[] path);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_add_option(IntPtr media, byte[] option);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_release(IntPtr media);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_set_media(IntPtr player, IntPtr media);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_media_player_play(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_stop(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_set_pause(IntPtr player, int pause);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_media_player_get_state(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint libvlc_media_player_has_vout(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern long libvlc_media_player_get_length(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern long libvlc_media_player_get_time(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_media_player_set_time(IntPtr player, long value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_media_player_is_seekable(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_audio_set_volume(IntPtr player, int value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_audio_set_mute(IntPtr player, int enabled);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_media_player_add_slave(IntPtr player, int type, byte[] uri, [MarshalAs(UnmanagedType.I1)] bool select);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_video_get_spu_description(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr libvlc_audio_get_track_description(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_track_description_list_release(IntPtr list);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void libvlc_video_set_aspect_ratio(IntPtr player, byte[] ratio);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_video_get_spu(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_audio_get_track(IntPtr player);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_video_set_spu(IntPtr player, int id);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int libvlc_audio_set_track(IntPtr player, int id);
    }
}
