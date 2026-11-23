using System.IO;
using System.Windows;
using KineticLyrics;

namespace KineticWallpaper;

public partial class WallpaperWindow : Window
{
    private readonly CancellationTokenSource stop = new();
    private readonly Action<string> report;

    public WallpaperWindow(Action<string> report)
    {
        this.report = report;
        InitializeComponent();
        bool preview = Environment.GetCommandLineArgs().Contains("--preview", StringComparer.OrdinalIgnoreCase);
        if (preview)
        {
            Title = "Kinetic Wallpaper — preview";
            Width = 900; Height = 550;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = true;
        }
        else
        {
            SourceInitialized += (_, _) => DesktopWallpaperHost.Attach(this);
        }
        bool diagnostic = Environment.GetCommandLineArgs().Contains("--above-icons", StringComparer.OrdinalIgnoreCase);
        Loaded += (_, _) => report(preview
            ? "Preview window opened. Close the controller to exit."
            : diagnostic ? "Above-icons diagnostic active; desktop icons may be covered. Close this controller to exit."
            : "Attached to a desktop WorkerW; visibility is not verified. Close this controller to exit.");
        Loaded += async (_, _) => await MonitorAsync(stop.Token);
        Closed += (_, _) => stop.Cancel();
    }

    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
            Directory.CreateDirectory(dir);
            using var library = new MusicLibrary(Path.Combine(dir, "library.db"));
            await MediaMonitor.RunAsync(library, state =>
            {
                if (ct.IsCancellationRequested) return;
                TrackText.Text = string.IsNullOrEmpty(state.Title) ? "" : $"{state.Title} — {state.Artist}";
                SourceText.Text = state.Lyrics?.Source ?? (string.IsNullOrEmpty(state.Title) ? "" : "No synchronized lyrics found");
                LyricsText.Text = string.IsNullOrEmpty(state.Title) ? "Waiting for music…" :
                    state.LineIndex >= 0 && state.Lyrics is not null ? state.Lyrics.Lines[state.LineIndex].Text : "♪";
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { LyricsText.Text = "Media monitoring stopped"; report("Media error: " + ex.Message); } }
    }
}
