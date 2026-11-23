# Desktop wallpaper prototype

Requires Windows 10/11, Explorer desktop, and .NET 10 SDK. The two project folders must remain side by side.

1. Optionally index your music first: `dotnet run --project KineticLyrics` and enter folders at the prompts. Both projects use the same `%LOCALAPPDATA%\KineticLyrics\library.db`.
2. Run `dotnet run --project KineticWallpaper` from the parent directory.
3. Play music in a player that publishes a Windows media session. A dark wallpaper with the track and synced lyric line should appear behind desktop icons. If you skip indexing, online lyrics may still work.
4. Keep the small controller window open; close it or click Exit to stop. Your normal Windows wallpaper remains unchanged.

This uses undocumented Explorer WorkerW behavior and is experimental: visibility is not verified and it may not work on all Windows versions, including Windows 11 24H2+. It attempts to span the virtual desktop and updates its size when display settings change; mixed-DPI and multi-monitor behavior still need Windows testing. Explorer restarts and audio visualization are not handled. The background is opaque, not an overlay on your existing wallpaper. The app does not change the Windows wallpaper setting; on exit it asks Explorer to redraw. Timeline precision depends on the player. See KineticLyrics/README.md for lyric matching and format limitations.

For a normal window without desktop attachment, run `dotnet run --project KineticWallpaper -- --preview`.
