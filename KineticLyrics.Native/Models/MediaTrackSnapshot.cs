namespace KineticLyrics.Native.Models;

public sealed record MediaTrackSnapshot(
    string Title,
    string Artist,
    string Album,
    string Source,
    bool IsPlaying,
    double PositionSeconds,
    double DurationSeconds,
    DateTimeOffset PositionTimestamp,
    byte[]? Artwork = null,
    string? MediaUri = null)
{
    public string Identity => $"{Artist.Trim()}|{Title.Trim()}";

    public double CurrentPositionSeconds(DateTimeOffset now)
    {
        if (!IsPlaying)
            return Math.Max(0, PositionSeconds);

        var elapsed = Math.Max(0, (now - PositionTimestamp).TotalSeconds);
        var position = Math.Max(0, PositionSeconds + elapsed);
        return DurationSeconds > 0 ? Math.Min(position, DurationSeconds) : position;
    }
}
