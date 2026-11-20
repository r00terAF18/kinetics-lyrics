namespace KineticLyrics.Native.Models;

public sealed record LyricLine(double TimeSeconds, string Text, IReadOnlyList<double>? WordTimes = null)
{
    public bool IsInstrumental => string.IsNullOrWhiteSpace(Text);
}

public sealed record LyricsDocument(IReadOnlyList<LyricLine> Lines, string Source)
{
    public int CurrentLineIndex(double positionSeconds)
    {
        var lo = 0;
        var hi = Lines.Count - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (Lines[mid].TimeSeconds <= positionSeconds)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best;
    }
}
