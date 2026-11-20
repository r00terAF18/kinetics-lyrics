using Android.Content;
using Android.Media;
using Android.Provider;
using ATL;
using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;
using System.Text.RegularExpressions;
using Uri = Android.Net.Uri;

namespace KineticLyrics.Native.Platforms;

public sealed class LocalLyricsProvider : ILocalLyricsProvider
{
    private readonly SemaphoreSlim _catalogLock = new(1, 1);
    private string? _catalogRoot;
    private IReadOnlyList<LocalEntry> _catalog = [];
    private bool _isIndexing;

    public string AccessStatus => Preferences.Default.Get("android_music_tree", string.Empty).Length > 0
        ? _isIndexing ? "Scanning the selected music folder…" : "Music folder selected"
        : "Choose a music folder to search local lyrics";

    public async Task<string?> ChooseMusicFolderAsync(CancellationToken cancellationToken = default)
    {
        var result = await MainActivity.ChooseMusicFolderAsync();
        var tree = result?.Data;
        if (tree is null)
            return null;

        var flags = result!.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        Android.App.Application.Context.ContentResolver?.TakePersistableUriPermission(tree, flags & ActivityFlags.GrantReadUriPermission);
        Preferences.Default.Set("android_music_tree", tree.ToString());
        _catalogRoot = null;
        _catalog = [];
        _ = Task.Run(() => EnsureCatalogAsync(tree, CancellationToken.None), CancellationToken.None);
        return tree.LastPathSegment ?? "Selected folder";
    }

    public async Task<string?> FindLyricsAsync(MediaTrackSnapshot track, CancellationToken cancellationToken = default)
    {
        var direct = await ReadFromMediaUriAsync(track.MediaUri, cancellationToken).ConfigureAwait(false);
        if (direct is not null)
            return direct;

        var rootText = Preferences.Default.Get("android_music_tree", string.Empty);
        if (string.IsNullOrWhiteSpace(rootText))
            return null;
        var root = Uri.Parse(rootText);
        if (root is null)
            return null;
        await EnsureCatalogAsync(root, cancellationToken).ConfigureAwait(false);

        var title = Normalize(track.Title);
        var artistTitle = Normalize(track.Artist + " " + track.Title);
        var exactMetadata = _catalog.FirstOrDefault(entry => IsAudio(entry.Name) &&
            Normalize(entry.Title) == title &&
            (Normalize(entry.Artist) == Normalize(track.Artist) || Normalize(entry.Artist).Length == 0 || Normalize(track.Artist).Length == 0));
        var titleMetadata = _catalog.FirstOrDefault(entry => IsAudio(entry.Name) && Normalize(entry.Title) == title);
        var audio = exactMetadata ?? titleMetadata ?? _catalog.FirstOrDefault(entry => IsAudio(entry.Name) && NameMatches(entry.Name, title, artistTitle));
        if (audio is null)
            return null;

        var embedded = await ReadEmbeddedLyricsAsync(audio.Uri, cancellationToken).ConfigureAwait(false);
        if (embedded is not null)
            return embedded;

        var stem = Path.GetFileNameWithoutExtension(audio.Name);
        var sidecar = _catalog.FirstOrDefault(entry => entry.ParentId == audio.ParentId &&
            string.Equals(Path.GetFileNameWithoutExtension(entry.Name), stem, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetExtension(entry.Name), ".lrc", StringComparison.OrdinalIgnoreCase));
        return sidecar is null ? null : await ReadTextAsync(sidecar.Uri, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCatalogAsync(Uri root, CancellationToken cancellationToken)
    {
        var rootKey = root.ToString();
        if (_catalogRoot == rootKey && _catalog.Count > 0)
            return;
        await _catalogLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_catalogRoot == rootKey && _catalog.Count > 0)
                return;
            _isIndexing = true;
            try
            {
                var entries = await Task.Run(() => EnumerateTree(root, cancellationToken), cancellationToken).ConfigureAwait(false);
                _catalog = entries;
                _catalogRoot = rootKey;
            }
            finally { _isIndexing = false; }
        }
        finally { _catalogLock.Release(); }
    }

    private static IReadOnlyList<LocalEntry> EnumerateTree(Uri root, CancellationToken cancellationToken)
    {
        var output = new List<LocalEntry>();
        var pending = new Stack<string>();
        var rootId = DocumentsContract.GetTreeDocumentId(root);
        pending.Push(rootId);
        var resolver = Android.App.Application.Context.ContentResolver!;
        while (pending.Count > 0 && output.Count < 50000)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parentId = pending.Pop();
            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(root, parentId);
            using var cursor = resolver.Query(childrenUri, [
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
                DocumentsContract.Document.ColumnMimeType
            ], null, null, null);
            if (cursor is null)
                continue;

            var idColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDocumentId);
            var nameColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
            var mimeColumn = cursor.GetColumnIndex(DocumentsContract.Document.ColumnMimeType);
            while (cursor.MoveToNext() && output.Count < 50000)
            {
                var id = cursor.GetString(idColumn) ?? string.Empty;
                var name = cursor.GetString(nameColumn) ?? string.Empty;
                var mime = cursor.GetString(mimeColumn) ?? string.Empty;
                if (mime == DocumentsContract.Document.MimeTypeDir)
                {
                    pending.Push(id);
                    continue;
                }
                if (!IsAudio(name) && !name.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase))
                    continue;
                var docUri = DocumentsContract.BuildDocumentUriUsingTree(root, id);
                var tags = IsAudio(name) ? ReadTags(resolver, docUri, name) : (Path.GetFileNameWithoutExtension(name), string.Empty);
                output.Add(new LocalEntry(name, id, parentId, docUri, tags.Item1, tags.Item2));
            }
        }
        return output;
    }

    private static async Task<string?> ReadFromMediaUriAsync(string? mediaUri, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(mediaUri))
            return null;
        var uri = Uri.Parse(mediaUri);
        if (uri is null || uri.Scheme != "content")
            return null;
        return await ReadEmbeddedLyricsAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadEmbeddedLyricsAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver!;
            await using var source = resolver.OpenInputStream(uri);
            if (source is null)
                return null;
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return ReadEmbeddedLyrics(buffer);
        }
        catch { return null; }
    }

    private static string? ReadEmbeddedLyrics(System.IO.Stream stream)
    {
        try
        {
            var audio = new Track(stream);
            if (audio.Lyrics is null)
                return null;
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
        catch { }
        return null;
    }

    private static async Task<string?> ReadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            var resolver = Android.App.Application.Context.ContentResolver!;
            await using var input = resolver.OpenInputStream(uri);
            if (input is null)
                return null;
            using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch { return null; }
    }

    private static bool NameMatches(string name, string title, string artistTitle)
    {
        var stem = Normalize(Path.GetFileNameWithoutExtension(name));
        return stem == title || stem == artistTitle || (title.Length > 5 && stem.Contains(title, StringComparison.Ordinal));
    }

    private static bool IsAudio(string name) => AudioExtensions.Contains(Path.GetExtension(name));

    private static (string Title, string Artist) ReadTags(ContentResolver resolver, Uri uri, string fileName)
    {
        try
        {
            using var retriever = new MediaMetadataRetriever();
            retriever.SetDataSource(Android.App.Application.Context, uri);
            var title = retriever.ExtractMetadata(MetadataKey.Title);
            var artist = retriever.ExtractMetadata(MetadataKey.Artist);
            return (string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title, artist ?? string.Empty);
        }
        catch
        {
            return (Path.GetFileNameWithoutExtension(fileName), string.Empty);
        }
    }

    private static string Normalize(string text) => string.Join(' ', Regex.Split(text.ToLowerInvariant().Normalize(), @"[^\p{L}\p{N}]+")
        .Where(part => part.Length > 0));

    private sealed record LocalEntry(string Name, string DocumentId, string ParentId, Uri Uri, string Title, string Artist);

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".m4a", ".mp4", ".aac", ".ogg", ".opus", ".wma", ".wav", ".aiff", ".ape", ".wv", ".tta"
    };
}
