using ATL;
using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;
using System.Text.RegularExpressions;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KineticLyrics.Native.Platforms;

public sealed class LocalLyricsProvider : ILocalLyricsProvider
{
    private const string RootToken = "kinetic-lyrics-music-root";
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<CatalogEntry>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private bool _isIndexing;

    public string AccessStatus => Preferences.Default.Get("music_root_token", string.Empty).Length > 0
        ? _isIndexing ? "Searching local music for lyrics…" : "Selected music folder and Music library are searched for local lyrics"
        : _isIndexing ? "Searching your Music folder for lyrics…" : "Your Music folder is searched for local lyrics";

    public async Task<string?> ChooseMusicFolderAsync(CancellationToken cancellationToken = default)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        var nativeWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (nativeWindow is null)
            throw new InvalidOperationException("The Windows folder picker could not find the app window.");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(nativeWindow));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return null;

        StorageApplicationPermissions.FutureAccessList.AddOrReplace(RootToken, folder);
        Preferences.Default.Set("music_root_token", RootToken);
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _catalogs.Remove(Path.GetFullPath(folder.Path)); }
        finally { _indexLock.Release(); }
        return folder.Name;
    }

    public async Task<string?> FindLyricsAsync(MediaTrackSnapshot track, CancellationToken cancellationToken = default)
    {
        var directPath = TryGetLocalPath(track.MediaUri);
        if (directPath is not null && File.Exists(directPath))
        {
            var direct = await ReadFromAudioFileAsync(directPath, cancellationToken).ConfigureAwait(false);
            if (direct is not null)
                return direct;
        }

        var exactKey = MakeKey(track.Artist, track.Title);
        foreach (var root in await GetSearchRootsAsync(cancellationToken).ConfigureAwait(false))
        {
            var catalog = await EnsureCatalogAsync(root, cancellationToken).ConfigureAwait(false);
            var candidate = catalog.FirstOrDefault(entry => entry.Key == exactKey)
                ?? catalog.FirstOrDefault(entry => FilenameMatches(entry.Path, track));
            if (candidate is null)
                continue;

            var lyrics = await ReadFromAudioFileAsync(candidate.Path, cancellationToken).ConfigureAwait(false);
            if (lyrics is not null)
                return lyrics;
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> GetSearchRootsAsync(CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        var token = Preferences.Default.Get("music_root_token", string.Empty);
        if (token.Length > 0)
        {
            try
            {
                var selectedFolder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
                if (Directory.Exists(selectedFolder.Path))
                    roots.Add(selectedFolder.Path);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Windows' global media-session metadata does not include the playing file's path.
        // Search the user's Music library automatically, in addition to any selected folder.
        var musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrWhiteSpace(musicFolder) && Directory.Exists(musicFolder))
            roots.Add(musicFolder);

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<CatalogEntry>> EnsureCatalogAsync(string root, CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogs.TryGetValue(root, out var existing))
                return existing;

            _isIndexing = true;
            try
            {
                var entries = await Task.Run(() =>
                {
                    var found = new List<CatalogEntry>();
                    foreach (var path in EnumerateAudioFiles(root, cancellationToken).Take(30000))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var title = Path.GetFileNameWithoutExtension(path);
                        var artist = string.Empty;
                        try
                        {
                            var metadata = new Track(path);
                            if (!string.IsNullOrWhiteSpace(metadata.Title))
                                title = metadata.Title;
                            artist = metadata.Artist;
                        }
                        catch { }
                        found.Add(new CatalogEntry(path, MakeKey(artist, title)));
                    }
                    return found;
                }, cancellationToken).ConfigureAwait(false);
                _catalogs[root] = entries;
                return entries;
            }
            finally
            {
                _isIndexing = false;
            }
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private static IEnumerable<string> EnumerateAudioFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<string> directories = [];
            IEnumerable<string> files = [];
            try { directories = Directory.EnumerateDirectories(current); } catch { }
            try { files = Directory.EnumerateFiles(current); } catch { }
            foreach (var directory in directories)
                if (!Path.GetFileName(directory).StartsWith(".", StringComparison.Ordinal)) pending.Push(directory);
            foreach (var file in files)
                if (AudioExtensions.Contains(Path.GetExtension(file))) yield return file;
        }
    }

    private static async Task<string?> ReadFromAudioFileAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sidecar = Path.ChangeExtension(path, ".lrc");
            try
            {
                if (File.Exists(sidecar))
                {
                    var sidecarLyrics = File.ReadAllText(sidecar);
                    if (LrcParser.Parse(sidecarLyrics).Count > 1)
                        return sidecarLyrics;
                }
            }
            catch { }

            try
            {
                var audio = new Track(path);
                if (audio.Lyrics is not null)
                {
                    foreach (var lyrics in audio.Lyrics)
                    {
                        if (lyrics.SynchronizedLyrics is { Count: > 1 } synchronized)
                        {
                            var lrc = LrcParser.ToLrc(synchronized.Select(phrase => ((long)phrase.TimestampStart, phrase.Text)));
                            if (LrcParser.Parse(lrc).Count > 1)
                                return lrc;
                        }

                        var embedded = lyrics.UnsynchronizedLyrics;
                        if (!string.IsNullOrWhiteSpace(embedded) && LrcParser.Parse(embedded).Count > 1)
                            return embedded;
                    }
                }

                return null;
            }
            catch { return null; }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool FilenameMatches(string path, MediaTrackSnapshot track)
    {
        var stem = Normalize(Path.GetFileNameWithoutExtension(path));
        var title = Normalize(track.Title);
        var artistTitle = Normalize(track.Artist + " " + track.Title);
        return stem == title || stem == artistTitle || stem.Contains(title, StringComparison.Ordinal) && title.Length > 5;
    }

    private static string MakeKey(string? artist, string? title) => Normalize((artist ?? string.Empty) + "|" + (title ?? string.Empty));

    private static string Normalize(string text) => string.Join(' ', Regex.Split(text.ToLowerInvariant().Normalize(), @"[^\p{L}\p{N}]+")
        .Where(part => part.Length > 0));

    private static string? TryGetLocalPath(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile) return parsed.LocalPath;
        return File.Exists(uri) ? uri : null;
    }

    private sealed record CatalogEntry(string Path, string Key);

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".mp4", ".aac", ".ogg", ".opus", ".wma", ".wav", ".aiff", ".ape", ".wv", ".tta"
    };
}
