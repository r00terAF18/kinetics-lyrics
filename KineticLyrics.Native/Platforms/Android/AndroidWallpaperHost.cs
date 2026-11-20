using Android.Content;
using KineticLyrics.Native.Services;

namespace KineticLyrics.Native.Platforms;

public sealed class AndroidWallpaperHost : IWallpaperHost
{
    public event EventHandler<string>? StatusChanged;

    public string Status => "Preview the wallpaper, then choose Set wallpaper to enable it.";

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshLyricsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void SetLyricsOffset(double seconds)
    {
        Preferences.Default.Set("lyrics_offset_seconds", seconds);
        StatusChanged?.Invoke(this, "Lyrics timing saved for the live wallpaper.");
    }

    internal static void OpenWallpaperPicker()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
            ?? throw new InvalidOperationException("The Android activity is not available.");
        var component = new ComponentName(activity, Java.Lang.Class.FromType(typeof(KineticLyricsWallpaperService)));
        var intent = new Intent("android.service.wallpaper.CHANGE_LIVE_WALLPAPER");
        intent.PutExtra("android.service.wallpaper.extra.LIVE_WALLPAPER_COMPONENT", component);
        activity.StartActivity(intent);
    }
}
