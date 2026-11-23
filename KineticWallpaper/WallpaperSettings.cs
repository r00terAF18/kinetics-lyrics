using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace KineticWallpaper;

public sealed class WallpaperSettings
{
    public double GlobalOffsetMs { get; set; } = 0;
    public double BeatReactivity { get; set; } = 0.65;
    public double Chaos { get; set; } = 0.35;
    public bool AutoAccent { get; set; } = true;
    public bool ShowIdleClock { get; set; } = true;
    public bool Clock24Hour { get; set; } = true;
    public bool ShowSongInfo { get; set; } = true;
    public bool BlurredAlbumBg { get; set; } = true;
    public double BackgroundDim { get; set; } = 0.55;
    public double HudLiftPercent { get; set; } = 0.065;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KineticLyrics", "settings.json");

    public static WallpaperSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<WallpaperSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("KineticLyricsWallpaper") is string;
        }
        catch { return false; }
    }

    public static void SetStartupEnabled(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key is null) return;
            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue("KineticLyricsWallpaper", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("KineticLyricsWallpaper", false);
            }
        }
        catch { }
    }
}
