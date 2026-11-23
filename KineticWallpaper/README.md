# Desktop wallpaper prototype

Requires Windows 10/11, Explorer desktop, and .NET 10 SDK. The two project folders must remain side by side.

1. Optionally index your music first: `dotnet run --project KineticLyrics` and enter folders at the prompts. Both projects use the same `%LOCALAPPDATA%\KineticLyrics\library.db`.
2. Run `dotnet run --project KineticWallpaper` from the parent directory.
3. Play music in a player that publishes a Windows media session. A dark wallpaper with the track and synced lyric line should appear behind desktop icons. If you skip indexing, online lyrics may still work.
4. Keep the small controller window open; close it or click Exit to stop. Your normal Windows wallpaper remains unchanged.

This uses undocumented Explorer WorkerW behavior and is experimental: it may not work on all Windows versions or desktop configurations. Primary monitor only; it does not handle Explorer restarts, screen/DPI changes, multiple monitors, or audio visualization. The background is opaque, not an overlay on your existing wallpaper. Timeline precision depends on the player. See KineticLyrics/README.md for lyric matching and format limitations.
