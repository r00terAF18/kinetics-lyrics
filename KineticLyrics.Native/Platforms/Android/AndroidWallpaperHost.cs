using Android.Content;
using KineticLyrics.Native.Services;

namespace KineticLyrics.Native.Platforms;

public sealed class AndroidWallpaperHost : IWallpaperHost
{
    private const string LyricsRefreshGenerationKey = "android_lyrics_refresh_generation";

    public event EventHandler<string>? StatusChanged;

    public string Status => "Preview the wallpaper, then choose Set wallpaper to enable it.";

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshLyricsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var generation = Preferences.Default.Get(LyricsRefreshGenerationKey, 0);
        Preferences.Default.Set(LyricsRefreshGenerationKey, unchecked(generation + 1));
        return Task.CompletedTask;
    }

    public void SetLyricsOffset(double seconds)
    {
        Preferences.Default.Set("lyrics_offset_seconds", seconds);
        StatusChanged?.Invoke(this, "Lyrics timing saved for the live wallpaper.");
    }

    internal static void OpenWallpaperPicker()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("The Android activity is not available.");
        var component = new ComponentName(activity, "com.kineticslyrics.app.KineticLyricsWallpaperService");
        var intent = new Intent("android.service.wallpaper.CHANGE_LIVE_WALLPAPER");
        intent.PutExtra("android.service.wallpaper.extra.LIVE_WALLPAPER_COMPONENT", component);
        activity.StartActivity(intent);
    }
}
