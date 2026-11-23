using Windows.Media.Control;

namespace KineticLyrics;

public static class MediaMonitor
{
    public static async Task RunAsync(MusicLibrary library, CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        string? key = null; Lyrics? lyrics = null; int lastLine = -2;
        Console.WriteLine("Watching media sessions. Play a song in a supported player.");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var session = manager.GetCurrentSession();
            if (session is null)
            {
                if (key is not null) { Console.WriteLine("No media session."); key = null; lyrics = null; }
                await Task.Delay(1000, ct); continue;
            }
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                var newKey = $"{session.SourceAppUserModelId}\n{properties.Title}\n{properties.Artist}\n{properties.AlbumTitle}";
                if (newKey != key)
                {
                    key = newKey; lastLine = -2; lyrics = null;
                    Console.WriteLine($"\n{properties.Title} — {properties.Artist} ({properties.AlbumTitle})");
                    var track = library.Match(properties.Title ?? "", properties.Artist ?? "", properties.AlbumTitle ?? "", timeline.EndTime.TotalSeconds);
                    Console.WriteLine(track is null ? "No unique local match; trying online." : $"Probable local file: {track.Path}");
                    lyrics = await LyricsResolver.ResolveAsync(track, properties.Title ?? "", properties.Artist ?? "", ct);
                    Console.WriteLine(lyrics is null ? "No synchronized lyrics found." : $"Lyrics: {lyrics.Source}");
                    // Re-read current state after network I/O so stale lyrics are never displayed for a new track.
                    continue;
                }
                if (lyrics is not null && playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    var pos = timeline.Position.TotalSeconds;
                    int index = -1;
                    for (int i = 0; i < lyrics.Lines.Count && lyrics.Lines[i].Time <= pos; i++) index = i;
                    if (index != lastLine) { lastLine = index; if (index >= 0) Console.WriteLine($"[{TimeSpan.FromSeconds(pos):mm\\:ss}] {lyrics.Lines[index].Text}"); }
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { Console.WriteLine($"Media session changed: {ex.Message}"); key = null; lyrics = null; }
            await Task.Delay(250, ct);
        }
    }
}
