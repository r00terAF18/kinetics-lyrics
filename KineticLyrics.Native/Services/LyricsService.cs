using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using KineticLyrics.Native.Models;

namespace KineticLyrics.Native.Services;

public sealed class LyricsService(ILocalLyricsProvider localProvider)
{
    private static readonly HttpClient Http = CreateHttpClient();
    private int _requestId;

    public async Task<LyricsDocument?> FindAsync(MediaTrackSnapshot track, CancellationToken cancellationToken = default)
    {
        var requestId = Interlocked.Increment(ref _requestId);

        try
        {
            var local = await localProvider.FindLyricsAsync(track, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(local) && IsCurrent(requestId))
            {
                var parsed = LrcParser.Parse(local);
                if (parsed.Count > 1)
                    return new LyricsDocument(parsed, "local lyrics");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        var lrclib = await FindLrclibAsync(track, cancellationToken).ConfigureAwait(false);
        if (lrclib is not null && IsCurrent(requestId))
            return lrclib;

        var netease = await FindNeteaseAsync(track, cancellationToken).ConfigureAwait(false);
        return IsCurrent(requestId) ? netease : null;
    }

    private bool IsCurrent(int requestId) => Volatile.Read(ref _requestId) == requestId;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KineticLyricsNative/1.0");
        return client;
    }

    private static async Task<LyricsDocument?> FindLrclibAsync(MediaTrackSnapshot track, CancellationToken cancellationToken)
    {
        var title = Clean(track.Title);
        if (title.Length == 0)
            return null;

        var queries = new[]
        {
            $"https://lrclib.net/api/search?track_name={Q(track.Title)}&artist_name={Q(track.Artist)}",
            $"https://lrclib.net/api/search?track_name={Q(title)}&artist_name={Q(FirstArtist(track.Artist))}",
            $"https://lrclib.net/api/search?q={Q(title + " " + FirstArtist(track.Artist))}"
        };

        foreach (var url in queries)
        {
            try
            {
                using var stream = await Http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var candidates = document.RootElement.EnumerateArray()
                    .Select(item => new
                    {
                        Item = item,
                        Score = Similarity(GetString(item, "trackName"), track.Title) * 3 + Similarity(GetString(item, "artistName"), track.Artist) * 2
                    })
                    .OrderByDescending(candidate => candidate.Score);

                foreach (var candidate in candidates)
                {
                    if (candidate.Score < 2.8)
                        continue;
                    var synced = GetString(candidate.Item, "syncedLyrics");
                    var lines = LrcParser.Parse(synced);
                    if (lines.Count > 1)
                        return new LyricsDocument(lines, "LRCLIB");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        return null;
    }

    private static async Task<LyricsDocument?> FindNeteaseAsync(MediaTrackSnapshot track, CancellationToken cancellationToken)
    {
        try
        {
            var searchUrl = $"https://music.163.com/api/search/get?s={Q(Clean(track.Title) + " " + FirstArtist(track.Artist))}&type=1&limit=6";
            using var searchStream = await Http.GetStreamAsync(searchUrl, cancellationToken).ConfigureAwait(false);
            using var search = await JsonDocument.ParseAsync(searchStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!search.RootElement.TryGetProperty("result", out var result) || !result.TryGetProperty("songs", out var songs))
                return null;

            var best = songs.EnumerateArray()
                .Select(song => new
                {
                    Id = song.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                    Title = GetString(song, "name"),
                    Artists = song.TryGetProperty("artists", out var artists)
                        ? string.Join(", ", artists.EnumerateArray().Select(artist => GetString(artist, "name")))
                        : string.Empty
                })
                .Select(song => (song.Id, song.Title, song.Artists, Score: Similarity(song.Title, track.Title) * 3 + Similarity(song.Artists, track.Artist) * 2))
                .Where(song => song.Id > 0)
                .OrderByDescending(song => song.Score)
                .FirstOrDefault();

            if (best.Id <= 0 || best.Score < 2.8)
                return null;

            var lyricUrl = $"https://music.163.com/api/song/lyric/v1?id={best.Id}&lv=1&kv=1&tv=-1&yv=1&rv=1";
            using var lyricStream = await Http.GetStreamAsync(lyricUrl, cancellationToken).ConfigureAwait(false);
            using var lyrics = await JsonDocument.ParseAsync(lyricStream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var lrc = lyrics.RootElement.TryGetProperty("lrc", out var lrcElement) ? GetString(lrcElement, "lyric") : null;
            var parsed = LrcParser.Parse(lrc);
            return parsed.Count > 1 ? new LyricsDocument(parsed, "NetEase") : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static string GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string FirstArtist(string artist) => artist.Split([',', '&'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? artist;

    private static string Clean(string value) => value.Split(['(', '['], 2)[0].Trim();

    private static string Q(string value) => WebUtility.UrlEncode(value);

    private static double Similarity(string left, string right)
    {
        static string Normalize(string value) => string.Join(' ', Regex.Split(value.ToLowerInvariant().Normalize(), @"[^\p{L}\p{N}]+")
            .Where(part => part.Length > 0));
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
            return 0;
        if (a == b)
            return 1;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
            return .8;
        var aWords = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var bWords = b.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return .7 * bWords.Count(aWords.Contains) / Math.Max(aWords.Count, bWords.Length);
    }
}
