using System.Globalization;
using System.Text.RegularExpressions;
using KineticLyrics.Native.Models;

namespace KineticLyrics.Native.Services;

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex LineStampRegex();

    [GeneratedRegex(@"<(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)>([^<]*)")]
    private static partial Regex WordStampRegex();

    [GeneratedRegex(@"\[(\w+):([^\]]*)\]")]
    private static partial Regex MetadataRegex();

    public static IReadOnlyList<LyricLine> Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return [];

        var lines = new List<LyricLine>();
        foreach (var raw in source.Replace("\uFEFF", string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var stamps = LineStampRegex().Matches(raw);
            if (stamps.Count == 0)
                continue;

            var body = LineStampRegex().Replace(raw, string.Empty);
            body = MetadataRegex().Replace(body, string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(body) || IsCreditLine(body))
                continue;

            var wordMatches = WordStampRegex().Matches(body);
            string text;
            IReadOnlyList<double>? wordTimes = null;
            if (wordMatches.Count > 0)
            {
                var words = new List<string>();
                var times = new List<double>();
                foreach (Match match in wordMatches)
                {
                    var phrase = match.Groups[3].Value.Trim();
                    foreach (var word in Regex.Split(phrase, @"\s+").Where(part => part.Length > 0))
                    {
                        words.Add(word);
                        times.Add(ParseTime(match.Groups[1].Value, match.Groups[2].Value));
                    }
                }
                text = string.Join(' ', words).Trim();
                wordTimes = times;
            }
            else
            {
                text = body;
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (Match stamp in stamps)
            {
                lines.Add(new LyricLine(ParseTime(stamp.Groups[1].Value, stamp.Groups[2].Value), text, wordTimes));
            }
        }

        return lines.OrderBy(line => line.TimeSeconds).ToArray();
    }

    public static string ToLrc(IEnumerable<(long Milliseconds, string Text)> phrases)
    {
        var lines = phrases
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase.Text))
            .Select(phrase => $"[{FormatTime(phrase.Milliseconds / 1000d)}]{phrase.Text.Trim()}");
        return string.Join(Environment.NewLine, lines);
    }

    private static double ParseTime(string minutes, string seconds)
    {
        var min = double.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 0;
        var sec = double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : 0;
        return min * 60 + sec;
    }

    private static string FormatTime(double seconds)
    {
        var minutes = (int)(seconds / 60);
        return $"{minutes:00}:{seconds - minutes * 60:00.000}";
    }

    private static bool IsCreditLine(string text) =>
        Regex.IsMatch(text, @"^(producer|composer|lyricist|lyrics by|written by|arrange|mix|master)\s*[:：]", RegexOptions.IgnoreCase);
}
