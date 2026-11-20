using KineticLyrics.Native.Models;

namespace KineticLyrics.Native.Services;

public sealed class MediaSessionService(IPlatformMediaSession source)
{
    public event EventHandler<MediaTrackSnapshot?>? TrackChanged;
    public string AccessStatus => source.AccessStatus;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        source.TrackChanged -= OnTrackChanged;
        source.TrackChanged += OnTrackChanged;
        return source.StartAsync(cancellationToken);
    }

    public Task RequestAccessAsync(CancellationToken cancellationToken = default) => source.RequestAccessAsync(cancellationToken);

    private void OnTrackChanged(object? sender, MediaTrackSnapshot? track) => TrackChanged?.Invoke(this, track);
}
