# FrameDeck

FrameDeck is a local video player for 64-bit Windows 10 or 11. It uses the native VLC 3 playback engine and has a dark media library designed for browsing with a keyboard.

## Run

1. Extract the full ZIP.
2. Double-click `FrameDeck.exe`.
3. Choose **Open video**, drag in a file, or use **Add folder** to index a folder and its subfolders.

FrameDeck uses an existing 64-bit VLC 3 installation. If playback reports that the engine is missing, install [VLC 3 for Windows](https://www.videolan.org/vlc/download-windows.html) and then reopen FrameDeck. For a portable setup, put an extracted 64-bit VLC 3 distribution in a `vlc` folder beside `FrameDeck.exe`.

| Shortcut | Action |
|---|---|
| Ctrl+O | Open a video |
| Ctrl+F | Search the library |
| Tab and Enter | Move between controls and activate a control |
| Space or K | Pause or resume |
| Left / Right | Seek backward / forward five seconds |
| J / L | Seek backward / forward ten seconds |
| Home | Go to the start of the video |
| Mouse wheel over the video, or Up / Down | Adjust volume |
| M | Mute or unmute |
| S | Stop playback |
| F or F11 | Toggle fullscreen |
| P | Toggle the always-on-top mini player |
| I | Toggle video-only view in any player mode |
| A | Cycle video aspect ratio |
| B | Cycle audio tracks |
| V | Cycle subtitles, including Off |
| Esc | Restore controls first, then exit fullscreen or mini player |

Select **Mini player** while watching to shrink the player and keep it on top of other windows. Use **Restore**, Esc, or the mini player's close button to return to the regular window. Playback continues as you switch modes. Use the **Video only** icon or I to hide controls and window chrome. Press I or Esc, or right-click the video, to bring the controls back.

FrameDeck automatically loads a subtitle with the same video name, such as Movie.srt or Movie.en.srt, from beside the video or a nearby Subs, Subtitles, or Captions folder. Use **Subtitles** to switch tracks, or choose **Browse for subtitle file** for a file with a different name or location. Dragging the timeline previews the new time and seeks when you release it. Use **Audio** to switch audio tracks. Video folders, recent history, and resume positions are saved in `%LOCALAPPDATA%\FrameDeck\library.json`.

## Movie library artwork

Select **Online artwork** in the sidebar and paste an [OMDb API key](https://www.omdbapi.com/apikey.aspx) for posters, plot summaries and IMDb ratings. Optionally add a TMDB API key or [TMDB API Read Access Token](https://www.themoviedb.org/settings/api) for another source of posters and movie details. Use **Test connection** in Online artwork to check both credentials and their poster downloads. Ratings are labeled IMDb or TMDB depending on the source. Select a poster or title to bring the movie into the spotlight, or click its play icon to watch. Use the genre chips to filter the library. The spotlight shows a large poster, the plot, and the resume position if one is saved. Click Details to read the full plot.

FrameDeck searches using a title and optional year, such as Dune (2021). It caches matches and artwork locally. Movies without an online match can use a local cover or a Windows video thumbnail, when available. Each page fills the available grid before adding another page.

This product uses the TMDB API but is not endorsed or certified by TMDB. Movie details and IMDb ratings are provided by OMDb where available.

## Build from source

Run `.\Source\Build.ps1` in PowerShell. The script uses the 64-bit .NET Framework C# compiler included with Windows. VLC is required for playback.

FrameDeck is an independent application. The ZIP contains the player and its source code.
