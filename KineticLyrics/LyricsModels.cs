namespace KineticLyrics;

public enum SectionType { Verse, PreChorus, Chorus, Bridge }

public record SongSection(SectionType Type, int Number, int LineIndexInSection, int TotalLinesInSection);

public record LyricWord(string Text, double StartTime, double EndTime);

public record LyricLine(
    double Time,
    double EndTime,
    string Text,
    IReadOnlyList<LyricWord> Words,
    string? AdLib = null,
    bool IsAdLibOnly = false,
    bool IsChorus = false,
    bool IsInstrumental = false,
    int RepeatIndex = 0,
    SongSection? Section = null);

public record Lyrics(string Source, bool IsWordSynced, IReadOnlyList<LyricLine> Lines);
