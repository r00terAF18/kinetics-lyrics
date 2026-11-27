using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;
using System.Xml.Linq;

namespace KineticLyrics;

public static partial class LyricsResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    [GeneratedRegex(@"\[(\d+):(\d{1,2}(?:\.\d+)?)\]")]
    private static partial Regex LineTimestampRegex();

    [GeneratedRegex(@"<(\d+):(\d{1,2}(?:\.\d+)?)>([^<]*)")]
    private static partial Regex WordTimestampRegex();

    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex AdLibRegex();

    [GeneratedRegex(@"\s*[\(\[].*?[\)\]]")]
    private static partial Regex BracketSuffixRegex();

    [GeneratedRegex(@"^(作词|作曲|编曲|制作|监制|混音|母带|和声|吉他|贝斯|鼓|录音|弦乐|出品|发行|producer|composer|lyricist|lyrics by|written by|arrange|mix|master)[^:：]{0,14}[:：]", RegexOptions.IgnoreCase)]
    private static partial Regex CreditLineRegex();

    [GeneratedRegex(@"[\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]")]
    private static partial Regex CjkRegex();

    public static Lyrics Parse(string rawLrc, string source)
    {
        var rawLines = new List<(double Time, string Text, List<(double Time, string Text)>? WordTimes)>();
        bool anyWordSynced = false;

        foreach (var raw in rawLrc.Split('\n'))
        {
            var matches = LineTimestampRegex().Matches(raw);
            if (matches.Count == 0) continue;

            var body = LineTimestampRegex().Replace(raw, "");
            List<(double Time, string Text)>? wt = null;

            var wordMatches = WordTimestampRegex().Matches(body);
            if (wordMatches.Count > 0)
            {
                wt = [];
                var sb = new StringBuilder();
                foreach (Match wm in wordMatches)
                {
                    if (!TryParseTime(wm.Groups[1].Value, wm.Groups[2].Value, out var wSec)) continue;
                    var chunk = wm.Groups[3].Value.Trim();
                    if (string.IsNullOrEmpty(chunk)) continue;
                    foreach (var token in Tokenize(chunk))
                    {
                        wt.Add((wSec, token));
                        if (sb.Length > 0 && !CjkRegex().IsMatch(token)) sb.Append(' ');
                        sb.Append(token);
                    }
                }
                body = sb.ToString();
                if (wt.Count > 0) anyWordSynced = true;
                else wt = null;
            }

            var cleanBody = body.Trim();
            if (CreditLineRegex().IsMatch(cleanBody)) continue;

            foreach (Match m in matches)
            {
                if (TryParseTime(m.Groups[1].Value, m.Groups[2].Value, out var sec))
                    rawLines.Add((sec, cleanBody, wt));
            }
        }

        rawLines.Sort((a, b) => a.Time.CompareTo(b.Time));
        var prepared = PrepareLines(rawLines);
        return new Lyrics(source, anyWordSynced, prepared);
    }

    public static bool TryReadLocalLrc(Track? track, out string source, out string rawLrc)
    {
        source = "";
        rawLrc = "";
        if (track is null || !File.Exists(track.Path)) return false;

        // Embedded tags take precedence over an adjacent sidecar.
        double duration = 0;
        try
        {
            using var audio = TagLib.File.Create(track.Path);
            duration = audio.Properties.Duration.TotalSeconds;
            var normalized = NormalizeLocalLyrics(audio.Tag.Lyrics, duration);
            if (normalized is not null)
            {
                source = "Embedded Tags";
                rawLrc = normalized;
                if (!LineTimestampRegex().IsMatch(audio.Tag.Lyrics ?? "")) source += " · estimated timing";
                return true;
            }
        }
        catch { }

        // Sidecars remain available offline when the audio file has no usable embedded lyrics.
        foreach (var extension in new[] { ".lrc", ".txt" })
        {
            var sidecar = Path.ChangeExtension(track.Path, extension);
            try
            {
                if (!File.Exists(sidecar)) continue;
                var text = File.ReadAllText(sidecar);
                var normalized = NormalizeLocalLyrics(text, duration);
                if (normalized is not null)
                {
                    source = extension == ".lrc" ? "Local .lrc" : "Local .txt";
                    rawLrc = normalized;
                    if (!LineTimestampRegex().IsMatch(text)) source += " · estimated timing";
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    private static string? NormalizeLocalLyrics(string? raw, double duration)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (LineTimestampRegex().IsMatch(raw)) return raw;

        var lines = raw.Split('\n')
            .Select(line => line.Trim().Trim('\r'))
            .Where(line => line.Length > 0 && !Regex.IsMatch(line, @"^\[(ar|ti|al|by|offset|re|ve):", RegexOptions.IgnoreCase))
            .ToList();
        if (lines.Count < 3) return null;

        var span = duration > lines.Count * 0.8 ? duration : lines.Count * 4.0;
        var step = span / lines.Count;
        return string.Join("\n", lines.Select((line, index) =>
        {
            var centiseconds = (long)Math.Round(index * step * 100, MidpointRounding.AwayFromZero);
            var minutes = centiseconds / 6000;
            var seconds = centiseconds % 6000 / 100;
            var fraction = centiseconds % 100;
            return $"[{minutes:00}:{seconds:00}.{fraction:00}]{line}";
        }));
    }

    public static async Task<Lyrics?> ResolveAsync(Track? track, string title, string artist, string album, double duration, CancellationToken ct)
    {
        if (track is not null)
        {
            try
            {
                using var audio = TagLib.File.Create(track.Path);
                if (!string.IsNullOrWhiteSpace(audio.Tag.Lyrics))
                {
                    var parsed = Parse(audio.Tag.Lyrics, "Embedded Tags");
                    if (parsed.Lines.Count > 2) return parsed;
                }
            }
            catch { }

            var sidecar = Path.ChangeExtension(track.Path, ".lrc");
            try
            {
                if (File.Exists(sidecar))
                {
                    var parsed = Parse(await File.ReadAllTextAsync(sidecar, ct), "Local .lrc");
                    if (parsed.Lines.Count > 2) return parsed;
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(title)) return null;

        var tasks = new List<Task<List<Candidate>>>
        {
            QueryLrclibAsync(title, artist, album, duration, ct),
            QueryNeteaseAsync(title, artist, duration, ct),
            QueryBetterLyricsAsync("kugou", title, artist, album, duration, ct),
            QueryBetterLyricsAsync("qq", title, artist, album, duration, ct)
        };

        await Task.WhenAll(tasks);
        var best = tasks
            .SelectMany(t => t.Result)
            .Select(c => (Candidate: c, Score: ScoreCandidate(c, title, artist, duration)))
            .Where(x => x.Score > 2.8)
            .OrderByDescending(x => Math.Round(x.Score * 2))
            .ThenByDescending(x => x.Candidate.Lrc.Contains('<'))
            .ThenBy(x => SourcePriority(x.Candidate.Source))
            .Select(x => x.Candidate)
            .FirstOrDefault();

        if (best is null) return null;
        var result = Parse(best.Lrc, best.Source);
        return result.Lines.Count > 2 ? result : null;
    }

    private record Candidate(string Source, string Title, string Artist, double Duration, string Lrc);

    private static async Task<List<Candidate>> QueryLrclibAsync(string title, string artist, string album, double duration, CancellationToken ct)
    {
        var results = new List<Candidate>();
        var cleanTitle = CleanTitle(title);
        var mainArtist = MainArtist(artist);

        var urls = new List<string>
        {
            $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}",
            $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(mainArtist)}",
            $"https://lrclib.net/api/search?q={Uri.EscapeDataString($"{cleanTitle} {mainArtist}".Trim())}"
        };

        if (!string.IsNullOrWhiteSpace(album) && duration > 0)
            urls.Add($"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}&album_name={Uri.EscapeDataString(album)}&duration={Math.Round(duration)}");

        foreach (var url in urls.Distinct())
        {
            try
            {
                using var resp = await Http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                        TryAddLrclibElement(el, results);
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    TryAddLrclibElement(doc.RootElement, results);
                }

                if (results.Count > 0) break;
            }
            catch when (!ct.IsCancellationRequested) { }
        }
        return results;
    }

    private static void TryAddLrclibElement(JsonElement el, List<Candidate> list)
    {
        if (!el.TryGetProperty("syncedLyrics", out var lrcEl) || lrcEl.ValueKind != JsonValueKind.String) return;
        var lrc = lrcEl.GetString();
        if (string.IsNullOrWhiteSpace(lrc)) return;
        var t = el.TryGetProperty("trackName", out var tn) ? tn.GetString() ?? "" : "";
        var a = el.TryGetProperty("artistName", out var an) ? an.GetString() ?? "" : "";
        var d = el.TryGetProperty("duration", out var du) && du.TryGetDouble(out var dv) ? dv : 0;
        list.Add(new Candidate("LRCLIB", t, a, d, lrc));
    }

    private static async Task<List<Candidate>> QueryNeteaseAsync(string title, string artist, double duration, CancellationToken ct)
    {
        var results = new List<Candidate>();
        try
        {
            var q = Uri.EscapeDataString($"{CleanTitle(title)} {MainArtist(artist)}".Trim());
            using var resp = await Http.GetAsync($"https://music.163.com/api/search/get?s={q}&type=1&limit=5", ct);
            if (!resp.IsSuccessStatusCode) return results;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("result", out var res) || !res.TryGetProperty("songs", out var songs))
                return results;

            var top = songs.EnumerateArray()
                .Select(s =>
                {
                    var id = s.GetProperty("id").GetInt64();
                    var name = s.GetProperty("name").GetString() ?? "";
                    var art = s.TryGetProperty("artists", out var ar)
                        ? string.Join(", ", ar.EnumerateArray().Select(x => x.GetProperty("name").GetString()))
                        : "";
                    var dur = s.TryGetProperty("duration", out var du) ? du.GetDouble() / 1000.0 : 0;
                    return (Id: id, Cand: new Candidate("NetEase", name, art, dur, ""));
                })
                .OrderByDescending(x => ScoreCandidate(x.Cand, title, artist, duration))
                .Take(2);

            foreach (var item in top)
            {
                using var lResp = await Http.GetAsync($"https://music.163.com/api/song/lyric/v1?id={item.Id}&lv=1&kv=1&tv=-1&yv=1&rv=1", ct);
                if (!lResp.IsSuccessStatusCode) continue;
                using var lDoc = JsonDocument.Parse(await lResp.Content.ReadAsStringAsync(ct));
                if (lDoc.RootElement.TryGetProperty("lrc", out var lrcObj) &&
                    lrcObj.TryGetProperty("lyric", out var lrcStr) &&
                    lrcStr.ValueKind == JsonValueKind.String)
                {
                    var text = lrcStr.GetString() ?? "";
                    if (LineTimestampRegex().IsMatch(text))
                        results.Add(item.Cand with { Lrc = text });
                }
            }
        }
        catch when (!ct.IsCancellationRequested) { }
        return results;
    }

    private static async Task<List<Candidate>> QueryBetterLyricsAsync(string provider, string title, string artist, string album, double duration, CancellationToken ct)
    {
        var results = new List<Candidate>();
        try
        {
            var query = new Dictionary<string, string>
            {
                ["s"] = CleanTitle(title),
                ["a"] = MainArtist(artist),
                ["al"] = album,
                ["d"] = duration > 0 ? Math.Round(duration).ToString(CultureInfo.InvariantCulture) : "0"
            };
            var qs = string.Join("&", query.Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value)}"));
            using var resp = await Http.GetAsync($"https://api.betterlyrics.org/{provider}/getLyrics?{qs}", ct);
            if (!resp.IsSuccessStatusCode) return results;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("lyrics", out var lyricsEl) || lyricsEl.ValueKind != JsonValueKind.String)
                return results;
            var raw = lyricsEl.GetString() ?? "";
            var lrc = provider == "qq" ? ConvertQrcToLrc(raw) : raw;
            if (!LineTimestampRegex().IsMatch(lrc)) return results;
            results.Add(new Candidate(provider == "qq" ? "QQ Music" : "Kugou", title, artist, duration, lrc));
        }
        catch when (!ct.IsCancellationRequested) { }
        return results;
    }

    private static string ConvertQrcToLrc(string raw)
    {
        string text = raw;
        try
        {
            var xml = XDocument.Parse(text);
            text = string.Join("\n", xml.DescendantNodes().OfType<XText>().Select(x => x.Value)
                .Concat(xml.Descendants().Attributes().Select(x => x.Value)));
        }
        catch
        {
            var lyricAttribute = Regex.Match(raw, "LyricContent=\"([^\"]*)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = lyricAttribute.Success
                ? WebUtility.HtmlDecode(lyricAttribute.Groups[1].Value)
                : Regex.Replace(raw, "</?QrcInfos[^>]*>", "\n", RegexOptions.IgnoreCase);
        }

        var lines = new List<string>();
        foreach (Match line in Regex.Matches(text, @"\[(\d+),(\d+)\]([^\[\r\n]*)"))
        {
            if (!long.TryParse(line.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startMs)) continue;
            var body = line.Groups[3].Value.Trim();
            if (string.IsNullOrWhiteSpace(body)) continue;

            var words = Regex.Matches(body, @"\((\d+),(\d+)(?:,\d+)?\)([^()]*)");
            if (words.Count == 0)
            {
                lines.Add($"[{FormatLrcTime(startMs)}]{body}");
                continue;
            }

            var chunks = new List<string>();
            foreach (Match word in words)
            {
                if (!long.TryParse(word.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wordMs)) continue;
                if (wordMs < startMs) wordMs += startMs;
                var value = word.Groups[3].Value;
                if (value.Length == 0) continue;
                chunks.Add($"<{FormatLrcTime(wordMs)}>{value}");
            }
            if (chunks.Count > 0)
                lines.Add($"[{FormatLrcTime(startMs)}]{string.Concat(chunks)}");
        }
        return string.Join("\n", lines);
    }

    private static string FormatLrcTime(long milliseconds)
    {
        var centiseconds = Math.Max(0, milliseconds) / 10;
        var minutes = centiseconds / 6000;
        var seconds = centiseconds % 6000 / 100;
        var fraction = centiseconds % 100;
        return $"{minutes:00}:{seconds:00}.{fraction:00}";
    }

    private static int SourcePriority(string source) => source switch
    {
        "LRCLIB" => 0,
        "NetEase" => 1,
        "QQ Music" => 2,
        "Kugou" => 3,
        _ => 4
    };

    private static IReadOnlyList<LyricLine> PrepareLines(List<(double Time, string Text, List<(double Time, string Text)>? WordTimes)> src)
    {
        var expanded = new List<(double Time, string Text, string? AdLib, bool AdLibOnly, List<(double Time, string Text)>? WordTimes)>();

        for (int i = 0; i < src.Count; i++)
        {
            var (time, text, wt) = src[i];
            string? adlib = null;
            bool adlibOnly = false;

            var adMatches = AdLibRegex().Matches(text);
            if (adMatches.Count > 0)
            {
                var extracted = string.Join(" · ", adMatches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0));
                var rest = AdLibRegex().Replace(text, " ").Trim();
                if (!string.IsNullOrEmpty(rest))
                {
                    text = rest;
                    adlib = extracted;
                    wt = null;
                }
                else
                {
                    text = extracted;
                    adlibOnly = true;
                }
            }

            var tokens = Tokenize(text);
            if (tokens.Count > 14)
            {
                int half = (int)Math.Ceiling(tokens.Count / 2.0);
                double nextT = i + 1 < src.Count ? src[i + 1].Time : time + 6.0;
                string sep = CjkRegex().IsMatch(text) ? "" : " ";
                var firstHalf = tokens.Take(half).ToList();
                var secondHalf = tokens.Skip(half).ToList();
                double midT = wt is not null && wt.Count == tokens.Count
                    ? wt[half].Time
                    : time + (nextT - time) * (firstHalf.Sum(x => x.Length) / (double)Math.Max(1, tokens.Sum(x => x.Length)));

                expanded.Add((time, string.Join(sep, firstHalf), adlib, adlibOnly, wt?.Take(half).ToList()));
                expanded.Add((midT, string.Join(sep, secondHalf), null, adlibOnly, wt?.Skip(half).ToList()));
            }
            else
            {
                expanded.Add((time, text, adlib, adlibOnly, wt));
            }
        }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in expanded)
        {
            var k = NormalizeKey(item.Text);
            if (k.Length > 0) counts[k] = counts.GetValueOrDefault(k) + 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var built = new List<LyricLine>();

        for (int i = 0; i < expanded.Count; i++)
        {
            var item = expanded[i];
            double nextT = i + 1 < expanded.Count ? expanded[i + 1].Time : item.Time + 5.0;
            double endT = Math.Max(item.Time + 0.4, nextT);

            var k = NormalizeKey(item.Text);
            int rep = 0;
            bool isChorus = false;
            if (k.Length > 0)
            {
                rep = seen.GetValueOrDefault(k);
                seen[k] = rep + 1;
                isChorus = counts.GetValueOrDefault(k) > 1 && Tokenize(item.Text).Count >= 2;
            }

            var words = BuildWordTimings(item.Text, item.Time, endT, item.WordTimes);
            built.Add(new LyricLine(item.Time, endT, item.Text, words, item.AdLib, item.AdLibOnly, isChorus, string.IsNullOrWhiteSpace(item.Text), rep));

            if (!string.IsNullOrWhiteSpace(item.Text) && i + 1 < expanded.Count && expanded[i + 1].Time - item.Time > 9.0)
            {
                double spokenDur = Math.Min(6.0, Tokenize(item.Text).Count * 0.45 + 2.5);
                double breakStart = item.Time + spokenDur;
                built.Add(new LyricLine(breakStart, expanded[i + 1].Time, "", [], IsInstrumental: true));
            }
        }

        return LabelSections(built);
    }

    private static IReadOnlyList<LyricWord> BuildWordTimings(string text, double t0, double t1, List<(double Time, string Text)>? explicitWt)
    {
        var tokens = Tokenize(text);
        if (tokens.Count == 0) return [];

        if (explicitWt is not null && explicitWt.Count == tokens.Count)
        {
            var list = new List<LyricWord>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                double ws = explicitWt[i].Time;
                double we = i + 1 < tokens.Count ? explicitWt[i + 1].Time : t1;
                list.Add(new LyricWord(tokens[i], ws, Math.Max(ws + 0.08, we)));
            }
            return list;
        }

        double span = Math.Min((t1 - t0) * 0.74, tokens.Count * 0.42);
        var weights = tokens.Select(w => w.Length + 2.0).ToArray();
        double totalWeight = weights.Sum();
        double acc = 0;
        var estimated = new List<LyricWord>(tokens.Count);

        for (int i = 0; i < tokens.Count; i++)
        {
            double ws = t0 + span * (acc / totalWeight);
            acc += weights[i];
            double we = i + 1 < tokens.Count ? t0 + span * (acc / totalWeight) : t1;
            estimated.Add(new LyricWord(tokens[i], ws, Math.Max(ws + 0.08, we)));
        }
        return estimated;
    }

    private static IReadOnlyList<LyricLine> LabelSections(List<LyricLine> lines)
    {
        var blocks = new List<(List<int> Indices, SectionType Type)>();
        List<int>? cur = null;

        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].IsInstrumental)
            {
                cur = null;
                continue;
            }
            if (cur is null || i == 0 || lines[i - 1].IsInstrumental || lines[i].Time - lines[i - 1].Time > 3.6)
            {
                cur = [];
                blocks.Add((cur, SectionType.Verse));
            }
            cur.Add(i);
        }

        for (int b = 0; b < blocks.Count; b++)
        {
            var indices = blocks[b].Indices;
            int chorusCount = indices.Count(idx => lines[idx].IsChorus);
            var type = chorusCount / (double)indices.Count >= 0.5 ? SectionType.Chorus : SectionType.Verse;
            blocks[b] = (indices, type);
        }

        for (int b = 0; b < blocks.Count - 1; b++)
        {
            var (indices, type) = blocks[b];
            var (nextIndices, nextType) = blocks[b + 1];
            double gap = lines[nextIndices[0]].Time - lines[indices[^1]].Time;
            if (nextType == SectionType.Chorus && gap < 7.0 && indices.Count <= 4 && (type == SectionType.Chorus || b > 0))
                blocks[b] = (indices, SectionType.PreChorus);
        }

        int chorusesSeen = 0;
        double totalSongTime = lines.Count > 0 ? lines[^1].Time : 1;
        for (int b = blocks.Count - 1; b >= 0; b--)
        {
            if (blocks[b].Type == SectionType.Chorus) chorusesSeen++;
        }

        if (chorusesSeen >= 2)
        {
            for (int b = blocks.Count - 1; b >= 1; b--)
            {
                if (blocks[b].Type == SectionType.Verse && lines[blocks[b].Indices[0]].Time > totalSongTime * 0.5)
                {
                    blocks[b] = (blocks[b].Indices, SectionType.Bridge);
                    break;
                }
            }
        }

        var counters = new Dictionary<SectionType, int>();
        var result = lines.ToArray();
        foreach (var (indices, type) in blocks)
        {
            int num = counters.GetValueOrDefault(type) + 1;
            counters[type] = num;
            for (int k = 0; k < indices.Count; k++)
            {
                int idx = indices[k];
                result[idx] = result[idx] with { Section = new SongSection(type, num, k, indices.Count) };
            }
        }
        return result;
    }

    public static List<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!CjkRegex().IsMatch(text))
            return [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            if (CjkRegex().IsMatch(ch.ToString()))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                tokens.Add(ch.ToString());
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private static double ScoreCandidate(Candidate c, string title, string artist, double duration)
    {
        double score = Similarity(c.Title, title) * 3.0 + Similarity(c.Artist, artist) * 2.0;
        if (duration > 0 && c.Duration > 0)
        {
            double diff = Math.Abs(c.Duration - duration);
            score += diff < 2 ? 1.5 : diff < 5 ? 0.8 : diff < 15 ? 0 : -1.5;
        }
        return score;
    }

    private static double Similarity(string a, string b)
    {
        a = NormalizeKey(CleanTitle(a));
        b = NormalizeKey(CleanTitle(b));
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1.0;
        if (a.Contains(b) || b.Contains(a)) return 0.8;
        var setA = a.Split(' ').ToHashSet();
        var setB = b.Split(' ');
        int overlap = setB.Count(setA.Contains);
        return 0.7 * overlap / Math.Max(setA.Count, setB.Length);
    }

    private static string CleanTitle(string s) => BracketSuffixRegex().Replace(s, "").Split(" - ")[0].Trim();
    private static string MainArtist(string a) => Regex.Split(a, @",|&|\sfeat\.?\s|\sft\.?\s|\sx\s", RegexOptions.IgnoreCase)[0].Trim();
    public static string NormalizeKey(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    private static bool TryParseTime(string minStr, string secStr, out double seconds)
    {
        seconds = 0;
        if (int.TryParse(minStr, out var m) && double.TryParse(secStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            seconds = m * 60.0 + s;
            return true;
        }
        return false;
    }
}
