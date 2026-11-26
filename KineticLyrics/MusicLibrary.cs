using Microsoft.Data.Sqlite;
using System.Text.RegularExpressions;

namespace KineticLyrics;

public record Track(string Path, string Title, string Artist, string Album, double Duration);

public sealed class MusicLibrary : IDisposable
{
    private readonly SqliteConnection db;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".flac", ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".wma" };

    public MusicLibrary(string path)
    {
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS tracks (path TEXT PRIMARY KEY COLLATE NOCASE, size INTEGER NOT NULL, modified INTEGER NOT NULL, title TEXT NOT NULL, artist TEXT NOT NULL, album TEXT NOT NULL, duration REAL NOT NULL)";
        cmd.ExecuteNonQuery();
    }

    public void Scan(string folder)
    {
        int scanned = 0, changed = 0, errors = 0;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        try
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", options))
            {
                if (!Extensions.Contains(System.IO.Path.GetExtension(path))) continue;
                scanned++;
                try
                {
                    var info = new FileInfo(path);
                    using var check = db.CreateCommand();
                    check.CommandText = "SELECT size, modified FROM tracks WHERE path=$p";
                    check.Parameters.AddWithValue("$p", path);
                    using var reader = check.ExecuteReader();
                    bool same = reader.Read() && reader.GetInt64(0) == info.Length && reader.GetInt64(1) == info.LastWriteTimeUtc.Ticks;
                    reader.Close();
                    if (same) continue;

                    using var audio = TagLib.File.Create(path);
                    using var cmd = db.CreateCommand();
                    cmd.CommandText = "INSERT OR REPLACE INTO tracks VALUES ($p,$s,$m,$t,$a,$b,$d)";
                    cmd.Parameters.AddWithValue("$p", path);
                    cmd.Parameters.AddWithValue("$s", info.Length);
                    cmd.Parameters.AddWithValue("$m", info.LastWriteTimeUtc.Ticks);
                    cmd.Parameters.AddWithValue("$t", audio.Tag.Title ?? "");
                    cmd.Parameters.AddWithValue("$a", audio.Tag.FirstPerformer ?? "");
                    cmd.Parameters.AddWithValue("$b", audio.Tag.Album ?? "");
                    cmd.Parameters.AddWithValue("$d", audio.Properties.Duration.TotalSeconds);
                    cmd.ExecuteNonQuery();
                    changed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
                {
                    errors++;
                    Console.WriteLine($"Skipped {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Scan incomplete: {ex.Message}");
        }
        Console.WriteLine($"Scan finished: {scanned} audio files, {changed} updated, {errors} skipped.");
    }

    public void RemoveFolder(string folder)
    {
        var prefix = System.IO.Path.GetFullPath(folder)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM tracks WHERE substr(path, 1, length($prefix)) COLLATE NOCASE = $prefix";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        cmd.ExecuteNonQuery();
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();

    public Track? Match(string title, string artist, string album, double duration)
    {
        var t = Normalize(title);
        var a = Normalize(artist);
        if (t.Length == 0 || a.Length == 0) return null;

        var matches = new List<(Track track, int score)>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT path,title,artist,album,duration FROM tracks";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var track = new Track(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4));
            if (Normalize(track.Title) != t || Normalize(track.Artist) != a || !File.Exists(track.Path)) continue;
            var diff = Math.Abs(duration - track.Duration);
            if (duration > 0 && track.Duration > 0 && diff > 10) continue;
            int score = (duration > 0 && track.Duration > 0 && diff <= 3 ? 2 : 0) + (album.Length > 0 && Normalize(track.Album) == Normalize(album) ? 1 : 0);
            matches.Add((track, score));
        }
        var ordered = matches.OrderByDescending(x => x.score).ToList();
        return ordered.Count == 0 || (ordered.Count > 1 && ordered[0].score == ordered[1].score) ? null : ordered[0].track;
    }

    public void Dispose() => db.Dispose();
}
