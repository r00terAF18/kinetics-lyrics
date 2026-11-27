using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace KineticWallpaper;

public sealed class WallpaperSettings
{
    public string Preset { get; set; } = "custom";
    public string Layout { get; set; } = "auto";
    public string Transition { get; set; } = "mixed";
    public int Chaos { get; set; } = 35;
    public bool Choreo { get; set; } = true;
    public bool Carry { get; set; } = true;
    public string Camera { get; set; } = "auto";
    public int CamAmount { get; set; } = 50;
    public string BgMode { get; set; } = "art";
    public int Dim { get; set; } = 55;
    public bool AutoAccent { get; set; } = true;
    public string Accent { get; set; } = "1 0.3 0.43";
    public bool Invert { get; set; } = false;
    public string Provider { get; set; } = "auto";
    public string MusixmatchApiKey { get; set; } = "";
    public int Offset { get; set; } = 0;
    public bool AutoSync { get; set; } = false;
    public int Reactivity { get; set; } = 60;
    public bool Flashes { get; set; } = true;
    public bool MoodOn { get; set; } = true;
    public bool ShowInfo { get; set; } = true;
    public int HudLift { get; set; } = 4;
    public bool IdleClock { get; set; } = true;
    public bool Clock24 { get; set; } = true;
    public bool LowPower { get; set; } = false;
    public string TargetMonitor { get; set; } = "PRIMARY";
    public string DisplayMode { get; set; } = "auto";
    public string Monitors { get; set; } = "";
    public int TargetFps { get; set; } = 60;
    public List<string> MusicFolders { get; set; } = [];

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

    public string ResolveTargetDeviceName()
    {
        if (string.Equals(TargetMonitor, "ALL", StringComparison.OrdinalIgnoreCase))
            return "";
        if (string.Equals(TargetMonitor, "PRIMARY", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(TargetMonitor))
            return System.Windows.Forms.Screen.PrimaryScreen?.DeviceName ?? "";
        return TargetMonitor;
    }

    public static string BuildAutoMonitorsString()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length <= 1) return "";
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        var parts = new List<string>();
        foreach (var s in screens)
        {
            string role = s == primary ? ":main" : "";
            parts.Add($"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}{role}");
        }
        return string.Join("; ", parts);
    }

    public Dictionary<string, object> ToWallpaperEngineProperties()
    {
        bool spanAll = string.Equals(TargetMonitor, "ALL", StringComparison.OrdinalIgnoreCase) && System.Windows.Forms.Screen.AllScreens.Length > 1;
        string monitors = spanAll ? (string.IsNullOrWhiteSpace(Monitors) ? BuildAutoMonitorsString() : Monitors) : "";
        string mode = spanAll ? "multi" : "single";

        return new Dictionary<string, object>
        {
            ["preset"] = new { value = Preset },
            ["layout"] = new { value = Layout },
            ["transition"] = new { value = Transition },
            ["chaos"] = new { value = Chaos },
            ["choreo"] = new { value = Choreo },
            ["carry"] = new { value = Carry },
            ["camera"] = new { value = Camera },
            ["camamount"] = new { value = CamAmount },
            ["bgmode"] = new { value = BgMode },
            ["dim"] = new { value = Dim },
            ["autoaccent"] = new { value = AutoAccent },
            ["accent"] = new { value = Accent },
            ["invert"] = new { value = Invert },
            ["provider"] = new { value = Provider },
            ["mmkey"] = new { value = MusixmatchApiKey },
            ["offset"] = new { value = Offset },
            ["autosync"] = new { value = AutoSync },
            ["reactivity"] = new { value = Reactivity },
            ["flashes"] = new { value = Flashes },
            ["moodon"] = new { value = MoodOn },
            ["showinfo"] = new { value = ShowInfo },
            ["hudlift"] = new { value = HudLift },
            ["idleclock"] = new { value = IdleClock },
            ["clock24"] = new { value = Clock24 },
            ["lowpower"] = new { value = LowPower },
            ["layoutmode"] = new { value = mode },
            ["monitors"] = new { value = monitors }
        };
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
