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
    private string? _indexedRoot;
    private IReadOnlyList<CatalogEntry> _catalog = [];
    private bool _isIndexing;

    public string AccessStatus => Preferences.Default.Get("music_root_token", string.Empty).Length > 0
        ? _isIndexing ? "Indexing the selected music folder…" : "Music folder selected"
        : "Choose a music folder to search local lyrics";

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
        _indexedRoot = null;
        _catalog = [];
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

        var token = Preferences.Default.Get("music_root_token", string.Empty);
        if (token.Length == 0)
            return null;

        StorageFolder folder;
        try
        {
            folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
        }
        catch
        {
            return null;
        }

        await EnsureCatalogAsync(folder.Path, cancellationToken).ConfigureAwait(false);
        var exactKey = MakeKey(track.Artist, track.Title);
        var candidate = _catalog.FirstOrDefault(entry => entry.Key == exactKey)
            ?? _catalog.FirstOrDefault(entry => FilenameMatches(entry.Path, track));
        if (candidate is null)
            return null;

        return await ReadFromAudioFileAsync(candidate.Path, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCatalogAsync(string root, CancellationToken cancellationToken)
    {
        if (_indexedRoot == root)
            return;
        await _indexLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_indexedRoot == root)
                return;
            await BuildCatalogAsync(root, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    private async Task BuildCatalogAsync(string root, CancellationToken cancellationToken)
    {
        _isIndexing = true;
        try
        {
            var entries = await Task.Run(() =>
            {
                var found = new List<CatalogEntry>();
                foreach (var path in EnumerateAudioFiles(root, cancellationToken).Take(30000))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var metadata = new Track(path);
                        var title = string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(path) : metadata.Title;
                        found.Add(new CatalogEntry(path, MakeKey(metadata.Artist, title)));
                    }
                    catch { }
                }
                return found;
            }, cancellationToken).ConfigureAwait(false);
            _catalog = entries;
            _indexedRoot = root;
        }
        finally
        {
            _isIndexing = false;
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

                var sidecar = Path.ChangeExtension(path, ".lrc");
                return File.Exists(sidecar) ? File.ReadAllText(sidecar) : null;
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
