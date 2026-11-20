using KineticLyrics.Native.Platforms;
using KineticLyrics.Native.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace KineticLyrics.Native;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp();

        builder.Services.AddSingleton<MediaSessionService>();
        builder.Services.AddSingleton<LyricsService>();
        builder.Services.AddSingleton<ILocalLyricsProvider, LocalLyricsProvider>();
        builder.Services.AddSingleton<IPlatformMediaSession, PlatformMediaSession>();
#if WINDOWS
        builder.Services.AddSingleton<IWallpaperHost, WallpaperHost>();
#elif ANDROID
        builder.Services.AddSingleton<IWallpaperHost, AndroidWallpaperHost>();
#else
        builder.Services.AddSingleton<IWallpaperHost, NoopWallpaperHost>();
#endif
        builder.Services.AddSingleton<MainPage>();
        return builder.Build();
    }
}
