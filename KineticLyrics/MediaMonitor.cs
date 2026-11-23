using System.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace KineticLyrics;

public record MediaSnapshot(
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    double InterpolatedPosition,
    double Duration,
    byte[]? ThumbnailBytes,
    Lyrics? Lyrics,
    string StatusText);

public sealed class MediaMonitor
{
    private readonly MusicLibrary _library;
    private double _basePos;
    private long _baseTicks;
    private double _lastReportedPos = -1;
    private long _lastReportTicks;
    private readonly List<double> _phaseResiduals = [];
    private double _phaseCorrection = 0.25;
    private bool _isPlaying;
    private double _duration;
    private string _title = "";
    private string _artist = "";
    private string _album = "";
    private byte[]? _thumbnail;
    private Lyrics? _lyrics;
    private string _status = "Waiting for media…";
    private string? _sessionKey;
    private int _requestVersion;

    public MediaMonitor(MusicLibrary library) => _library = library;

    public double CurrentPosition =>
        _basePos + (_isPlaying ? Stopwatch.GetElapsedTime(_baseTicks).TotalSeconds : 0) + _phaseCorrection;

    public MediaSnapshot GetSnapshot() =>
        new(_title, _artist, _album, _isPlaying, CurrentPosition, _duration, _thumbnail, _lyrics, _status);

    public async Task RunAsync(CancellationToken ct)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var session = manager.GetCurrentSession();
                if (session is null)
                {
                    if (_sessionKey is not null)
                    {
                        _sessionKey = null;
                        _title = "";
                        _artist = "";
                        _album = "";
                        _thumbnail = null;
                        _lyrics = null;
                        _isPlaying = false;
                        _status = "Idle — play a track";
                    }
                    await Task.Delay(400, ct);
                    continue;
                }

                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();

                var title = props.Title ?? "";
                var artist = props.Artist ?? "";
                var album = props.AlbumTitle ?? "";
                var newKey = $"{session.SourceAppUserModelId}\n{title}\n{artist}\n{album}";

                bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (playing != _isPlaying)
                {
                    _basePos = _basePos + (_isPlaying ? Stopwatch.GetElapsedTime(_baseTicks).TotalSeconds : 0);
                    _baseTicks = Stopwatch.GetTimestamp();
                    _isPlaying = playing;
                }

                if (timeline.EndTime.TotalSeconds > 0)
                    _duration = timeline.EndTime.TotalSeconds;

                UpdateTimeline(timeline.Position.TotalSeconds);

                if (newKey != _sessionKey && !string.IsNullOrWhiteSpace(title))
                {
                    _sessionKey = newKey;
                    _title = title;
                    _artist = artist;
                    _album = album;
                    _lyrics = null;
                    _thumbnail = await ReadThumbnailAsync(props.Thumbnail);
                    _status = "Searching lyrics…";

                    int reqId = ++_requestVersion;
                    _ = Task.Run(async () =>
                    {
                        var track = _library.Match(title, artist, album, _duration);
                        var resolved = await LyricsResolver.ResolveAsync(track, title, artist, album, _duration, ct);
                        if (reqId != _requestVersion) return;
                        _lyrics = resolved;
                        _status = resolved is null
                            ? "No synced lyrics"
                            : $"{resolved.Source}{(resolved.IsWordSynced ? " · word-synced" : "")}";
                    }, ct);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                _sessionKey = null;
                _isPlaying = false;
            }

            await Task.Delay(120, ct);
        }
    }

    private void UpdateTimeline(double reportedPos)
    {
        if (double.IsNaN(reportedPos) || reportedPos < 0 || reportedPos > 36000) return;
        long now = Stopwatch.GetTimestamp();

        if (_lastReportedPos < 0)
        {
            _lastReportedPos = reportedPos;
            _lastReportTicks = now;
            _basePos = reportedPos;
            _baseTicks = now;
            _phaseResiduals.Clear();
            return;
        }

        if (Math.Abs(reportedPos - _lastReportedPos) < 0.02) return;

        double dt = Stopwatch.GetElapsedTime(_lastReportTicks, now).TotalSeconds;
        double adv = reportedPos - _lastReportedPos;
        _lastReportedPos = reportedPos;
        _lastReportTicks = now;

        double clock = _basePos + (_isPlaying ? Stopwatch.GetElapsedTime(_baseTicks, now).TotalSeconds : 0);
        double est = clock + _phaseCorrection;
        bool seek = adv < -1.6 || adv > dt + 2.2 || est < reportedPos - 1.8 || est > reportedPos + 2.8;

        if (seek || !_isPlaying)
        {
            _basePos = reportedPos;
            _baseTicks = now;
            _phaseResiduals.Clear();
            _phaseCorrection = 0.25;
        }
        else
        {
            _phaseResiduals.Add(reportedPos - clock);
            if (_phaseResiduals.Count > 14) _phaseResiduals.RemoveAt(0);
            double maxLowerBound = _phaseResiduals.Max();
            _phaseCorrection = maxLowerBound + 1.15 / (_phaseResiduals.Count + 1);
        }
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef is null) return null;
        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            uint size = (uint)stream.Size;
            if (size == 0) return null;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
