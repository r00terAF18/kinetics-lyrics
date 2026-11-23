using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KineticLyrics;

public record LyricLine(double Time, string Text);
public record Lyrics(string Source, IReadOnlyList<LyricLine> Lines);

public static class LyricsResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly Regex Timestamp = new(@"\[(\d+):(\d{1,2}(?:\.\d+)?)\]", RegexOptions.Compiled);
    public static IReadOnlyList<LyricLine> Parse(string text)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in text.Split('\n'))
        {
            var matches = Timestamp.Matches(raw);
            var body = Regex.Replace(Timestamp.Replace(raw, ""), @"<\d+:\d+(?:\.\d+)?>", "").Trim();
            foreach (Match m in matches)
                if (double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var sec) && int.TryParse(m.Groups[1].Value, out var min))
                    lines.Add(new LyricLine(min * 60 + sec, body));
        }
        return lines.OrderBy(x => x.Time).ToArray();
    }
    public static async Task<Lyrics?> ResolveAsync(Track? track, string title, string artist, CancellationToken ct)
    {
        if (track is not null)
        {
            try
            {
                using var audio = TagLib.File.Create(track.Path);
                var text = audio.Tag.Lyrics;
                if (!string.IsNullOrWhiteSpace(text)) { var lines = Parse(text); if (lines.Count > 0) return new("Embedded lyrics", lines); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException) { Console.WriteLine($"Could not read embedded lyrics: {ex.Message}"); }
            var sidecar = Path.ChangeExtension(track.Path, ".lrc");
            try
            {
                if (File.Exists(sidecar)) { var lines = Parse(await File.ReadAllTextAsync(sidecar, ct)); if (lines.Count > 0) return new($"Sidecar: {sidecar}", lines); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"Could not read sidecar: {ex.Message}"); }
        }
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) return null;
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            using var response = await Http.GetAsync(url, ct); response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                // Only accept exact title and artist: do not silently choose a different recording.
                if (!item.TryGetProperty("trackName", out var name) || !item.TryGetProperty("artistName", out var performer) ||
                    !string.Equals(name.GetString(), title, StringComparison.OrdinalIgnoreCase) || !string.Equals(performer.GetString(), artist, StringComparison.OrdinalIgnoreCase)) continue;
                if (item.TryGetProperty("syncedLyrics", out var lrc) && lrc.ValueKind == JsonValueKind.String)
                { var lines = Parse(lrc.GetString()!); if (lines.Count > 0) return new("LRCLIB", lines); }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested) { Console.WriteLine($"Online lookup failed: {ex.Message}"); }
        return null;
    }
}
