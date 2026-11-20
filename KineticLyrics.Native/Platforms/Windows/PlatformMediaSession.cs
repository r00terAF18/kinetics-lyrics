using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace KineticLyrics.Native.Platforms;

public sealed class PlatformMediaSession : IPlatformMediaSession
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private CancellationTokenSource? _pollCancellation;
    private string? _lastIdentity;
    private string? _lastSource;
    private byte[]? _lastArtwork;
    private string? _lastAlbum;
    private string? _lastTitle;
    private string? _lastArtist;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public event EventHandler<MediaTrackSnapshot?>? TrackChanged;
    public string AccessStatus { get; private set; } = "Windows system media access is ready";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_pollCancellation is not null)
            return;

        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
            _manager.SessionsChanged += OnSessionsChanged;
            AccessStatus = "Listening to Windows media sessions";
            _pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = PollAsync(_pollCancellation.Token);
        }
        catch (Exception ex)
        {
            AccessStatus = $"Windows media session unavailable: {ex.Message}";
        }
    }

    public async Task RequestAccessAsync(CancellationToken cancellationToken = default)
    {
        await StartAsync(cancellationToken);
        await PublishCurrentAsync(cancellationToken);
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = PublishCurrentAsync();

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _ = PublishCurrentAsync();

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(450));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await PublishCurrentAsync(cancellationToken);
        }
        catch (OperationCanceledException) { }
    }

    private async Task PublishCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !await _updateLock.WaitAsync(0, cancellationToken))
            return;
        try
        {
            var session = SelectSession(_manager);
            if (session is null)
            {
                if (_lastIdentity is not null)
                {
                    _lastIdentity = null;
                    TrackChanged?.Invoke(this, null);
                }
                return;
            }

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var updated = timeline.LastUpdatedTime;
            var position = timeline.Position.TotalSeconds;
            if (playing && updated != default)
                position += Math.Max(0, (DateTimeOffset.UtcNow - updated).TotalSeconds);
            var duration = timeline.EndTime.TotalSeconds;
            var source = session.SourceAppUserModelId ?? string.Empty;
            var identity = source + "|" + session.GetHashCode() + "|" + Math.Round(duration, 1);

            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties is not null)
            {
                var changed = identity != _lastIdentity || properties.Title != _lastTitle || properties.Artist != _lastArtist || properties.AlbumTitle != _lastAlbum;
                if (changed)
                {
                    _lastIdentity = identity;
                    _lastSource = source;
                    _lastTitle = properties.Title ?? string.Empty;
                    _lastArtist = properties.Artist ?? properties.AlbumArtist ?? string.Empty;
                    _lastAlbum = properties.AlbumTitle ?? string.Empty;
                    _lastArtwork = await ReadArtworkAsync(properties.Thumbnail);
                }

                if (string.IsNullOrWhiteSpace(_lastTitle))
                    return;
                var snapshot = new MediaTrackSnapshot(
                    _lastTitle,
                    _lastArtist ?? string.Empty,
                    _lastAlbum ?? string.Empty,
                    _lastSource ?? source,
                    playing,
                    Math.Max(0, position),
                    Math.Max(0, duration),
                    DateTimeOffset.UtcNow,
                    _lastArtwork);
                TrackChanged?.Invoke(this, snapshot);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AccessStatus = $"Windows media session error: {ex.Message}";
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private static GlobalSystemMediaTransportControlsSession? SelectSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        var sessions = manager.GetSessions();
        foreach (var session in sessions)
        {
            if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return session;
        }
        return manager.GetCurrentSession() ?? sessions.FirstOrDefault();
    }

    private static async Task<byte[]?> ReadArtworkAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
            return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var size = checked((uint)Math.Min(stream.Size, 8 * 1024 * 1024));
            await reader.LoadAsync(size);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch { return null; }
    }
}
