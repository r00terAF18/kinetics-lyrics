using Windows.Media.Control;

namespace KineticLyrics;

public record PlaybackState(string Title, string Artist, string Album, bool IsPlaying,
    double Position, double Duration, Lyrics? Lyrics, int LineIndex);

public static class MediaMonitor
{
    public static Task RunAsync(MusicLibrary library, CancellationToken ct) => RunAsync(library, null, ct);

    public static async Task RunAsync(MusicLibrary library, Action<PlaybackState>? onUpdate, CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        string? key = null;
        Lyrics? lyrics = null;
        int lastLine = -2;
        if (onUpdate is null) Console.WriteLine("Watching media sessions. Play a song in a supported player.");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var session = manager.GetCurrentSession();
            if (session is null)
            {
                if (key is not null)
                {
                    key = null; lyrics = null; lastLine = -2;
                    if (onUpdate is null) Console.WriteLine("No media session.");
                    else onUpdate(new("", "", "", false, 0, 0, null, -1));
                }
                await Task.Delay(500, ct);
                continue;
            }
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                string title = properties.Title ?? "", artist = properties.Artist ?? "", album = properties.AlbumTitle ?? "";
                var newKey = $"{session.SourceAppUserModelId}\n{title}\n{artist}\n{album}";
                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                double position = timeline.Position.TotalSeconds, duration = timeline.EndTime.TotalSeconds;
                if (newKey != key)
                {
                    key = newKey; lastLine = -2; lyrics = null;
                    if (onUpdate is null) Console.WriteLine($"\n{title} — {artist} ({album})");
                    else onUpdate(new(title, artist, album, playing, position, duration, null, -1));
                    var track = library.Match(title, artist, album, duration);
                    if (onUpdate is null) Console.WriteLine(track is null ? "No unique local match; trying online." : $"Probable local file: {track.Path}");
                    lyrics = await LyricsResolver.ResolveAsync(track, title, artist, ct);
                    if (onUpdate is null) Console.WriteLine(lyrics is null ? "No synchronized lyrics found." : $"Lyrics: {lyrics.Source}");
                    continue; // Verify the session again after potentially slow lookup.
                }
                int index = -1;
                if (lyrics is not null)
                    for (int i = 0; i < lyrics.Lines.Count && lyrics.Lines[i].Time <= position; i++) index = i;
                if (index != lastLine)
                {
                    lastLine = index;
                    if (onUpdate is null && playing && lyrics is not null && index >= 0)
                        Console.WriteLine($"[{TimeSpan.FromSeconds(position):mm\\:ss}] {lyrics.Lines[index].Text}");
                }
                onUpdate?.Invoke(new(title, artist, album, playing, position, duration, lyrics, index));
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                if (onUpdate is null) Console.WriteLine($"Media session changed: {ex.Message}");
                else onUpdate(new("", "", "", false, 0, 0, null, -1));
                key = null; lyrics = null; lastLine = -2;
            }
            await Task.Delay(250, ct);
        }
    }
}
