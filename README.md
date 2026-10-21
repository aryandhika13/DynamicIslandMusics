# Dynamic Island Desktop

An iPhone-style Dynamic Island for the Windows desktop. This lightweight WPF application displays the currently playing song at the top center of your screen, complete with media controls and synchronized scrolling lyrics.

<!-- Add a screenshot or demo GIF here, e.g., ![Demo](docs/demo.gif) -->

## Features

- **Follows any music:** Displays the title, artist, and album art from any active media player, such as Spotify or a browser-based YouTube player.
- **Media controls:** Play, pause, skip to the next track, or go to the previous track directly from the island.
- **Dual view:** A compact, non-intrusive view and an expanded view that appears when you hover your mouse over the island.
- **Real-time lyrics:** Lyrics appear line by line, perfectly synced with the song's playback position.
- **Adjustable font size:** Customize the lyrics font size through the settings menu, and the island will automatically adjust its size.
- **Smooth animations:** Seamless transitions when the island appears, resizes, and closes.
- **Always on top:** The island stays above other windows without disrupting your workflow.

## Requirements

- Windows 10 version 1809 (build 17763) or newer
- [.NET SDK](https://dotnet.microsoft.com/download) 6 or newer
- Internet connection (required for fetching lyrics only)

## Getting Started

```bash
git clone https://github.com/aryandhika13/DynamicIslandMusics.git
cd DynamicIslandDesktop
dotnet run
```

You can also open the project in Visual Studio and run it by pressing **F5**.

## Usage

1. Play music in any application that supports Windows media controls. The song title and album art will instantly appear on the island.
2. Hover your mouse over the center of the island to open the expanded view, then use the available control buttons.
3. Click the lyrics button to display the lyrics, and click it again to return to the song title.
4. Click the icon on the right side of the island to open the menu:
   - **Settings** to adjust the lyrics font size.
   - **Exit** to close the application.

## Architecture

This project follows the **MVVM** pattern. The code is divided into layers with unidirectional dependency flow (pointing downwards):

```
View  ──►  ViewModel  ──►  Service  ──►  Model
```

| Layer | Function |
|---|---|
| **View** (`MainWindow`) | UI, animations, and user interaction. Contains no business logic. |
| **ViewModel** | Stores the displayed state (title, lyrics, playback status) and provides commands for user actions. |
| **Service** | Logic outside the UI: reads Windows media sessions and fetches lyrics from the internet. |
| **Model** | Simple data structures such as lyric lines, media info, and app settings. |
| **Helpers** | WPF-specific utilities, such as text-fit adjustments and image converters. |

With this separation, the logic can be modified or tested without touching the UI. Model, Service, and ViewModel do not depend on WPF types.

```
DynamicIslandDesktop/
├── Models/          Data: LyricLine, MediaInfo, AppSettings
├── Services/        MediaSessionService, LyricsService, ITicker
├── ViewModels/      MainViewModel
├── Helpers/         TextFit, DispatcherTicker, BytesToImageConverter
├── MainWindow.xaml      Island UI
└── MainWindow.xaml.cs   UI animations and events
```

## Technologies

- [WPF](https://learn.microsoft.com/dotnet/desktop/wpf/) & .NET
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) for MVVM
- Windows Media Transport Controls (`Windows.Media.Control`) for reading and controlling media
- [LRCLIB](https://lrclib.net) for synchronized lyrics

## Limitations

- Only runs on Windows 10 and 11.
- Lyrics depend on availability in LRCLIB, so not all songs may have them.
- Settings are not persisted and reset to default values every time the application starts.

## Acknowledgments

Lyrics provided by [LRCLIB](https://lrclib.net).