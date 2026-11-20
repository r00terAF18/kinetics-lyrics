using KineticLyrics.Native.Models;

namespace KineticLyrics.Native.Services;

public interface IPlatformMediaSession
{
    event EventHandler<MediaTrackSnapshot?>? TrackChanged;
    string AccessStatus { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RequestAccessAsync(CancellationToken cancellationToken = default);
}

public interface ILocalLyricsProvider
{
    string AccessStatus { get; }
    Task<string?> ChooseMusicFolderAsync(CancellationToken cancellationToken = default);
    Task<string?> FindLyricsAsync(MediaTrackSnapshot track, CancellationToken cancellationToken = default);
}
