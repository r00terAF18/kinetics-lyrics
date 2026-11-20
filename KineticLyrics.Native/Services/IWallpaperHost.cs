using KineticLyrics.Native.Models;

namespace KineticLyrics.Native.Services;

public interface IWallpaperHost
{
    event EventHandler<string>? StatusChanged;
    string Status { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RefreshLyricsAsync(CancellationToken cancellationToken = default);
    void SetLyricsOffset(double seconds);
}

public sealed class NoopWallpaperHost : IWallpaperHost
{
    public event EventHandler<string>? StatusChanged { add { } remove { } }
    public string Status => "Wallpaper rendering is currently available on Windows.";
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshLyricsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetLyricsOffset(double seconds) { }
}
