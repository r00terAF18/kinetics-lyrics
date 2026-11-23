using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using KineticLyrics;
using Windows.Media.Control;

namespace KineticWallpaper;

public partial class WallpaperWindow : Window
{
    private readonly CancellationTokenSource stop = new();
    private readonly Action<string> report;
    private MusicLibrary? library;
    public WallpaperWindow(Action<string> report)
    {
        this.report = report;
        InitializeComponent();
        SourceInitialized += Attach;
        Loaded += async (_, _) => await MonitorAsync(stop.Token);
        Closed += (_, _) => { stop.Cancel(); library?.Dispose(); };
    }
    private void Attach(object? sender, EventArgs e)
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) throw new InvalidOperationException("Windows desktop not found.");
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        IntPtr desktopLayer = IntPtr.Zero;
        IntPtr insertAfter = IntPtr.Zero;
        // Newer Explorer builds can host the icon view directly in Progman rather
        // than providing the separate top-level WorkerW used by older builds.
        var iconView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        bool modernDesktop = (GetWindowLong(progman, -20) & 0x00200000) != 0;
        if (modernDesktop && iconView != IntPtr.Zero)
        {
            desktopLayer = progman;
            insertAfter = iconView; // Place our sibling immediately behind the icons.
        }
        else
        {
            EnumWindows((window, _) =>
            {
                if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    desktopLayer = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            if (desktopLayer == IntPtr.Zero && iconView != IntPtr.Zero)
            {
                desktopLayer = progman;
                insertAfter = iconView;
            }
        }
        if (desktopLayer == IntPtr.Zero) throw new InvalidOperationException("Could not find a compatible desktop layer. Please report your Windows version and whether Explorer is running.");
        var handle = new WindowInteropHelper(this).Handle;
        // SetParent does not adjust the window style automatically.
        var style = GetWindowLong(handle, -16);
        SetWindowLong(handle, -16, (style & ~unchecked((int)0x80000000)) | 0x40000000);
        Marshal.SetLastPInvokeError(0);
        var previous = SetParent(handle, desktopLayer);
        if (previous == IntPtr.Zero && Marshal.GetLastPInvokeError() != 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        // This first prototype fills the primary monitor only.
        uint flags = 0x0010 | 0x0040; // no activation; show window
        if (insertAfter == IntPtr.Zero) flags |= 0x0004; // retain Z-order on the classic WorkerW path
        if (!SetWindowPos(handle, insertAfter, 0, 0, GetSystemMetrics(0), GetSystemMetrics(1), flags))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        report("Wallpaper active on the primary monitor. Close this window to exit.");
    }
    private async Task MonitorAsync(CancellationToken ct)
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
            Directory.CreateDirectory(dir);
            library = new MusicLibrary(System.IO.Path.Combine(dir, "library.db"));
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            string? key = null; Lyrics? lyrics = null;
            while (!ct.IsCancellationRequested)
            {
                var session = manager.GetCurrentSession();
                if (session is null)
                {
                    key = null; lyrics = null; TrackText.Text = ""; SourceText.Text = ""; LyricsText.Text = "Waiting for music…";
                    await Task.Delay(1000, ct); continue;
                }
                var props = await session.TryGetMediaPropertiesAsync();
                var timeline = session.GetTimelineProperties();
                var current = $"{session.SourceAppUserModelId}\n{props.Title}\n{props.Artist}\n{props.AlbumTitle}";
                if (key != current)
                {
                    key = current; lyrics = null;
                    TrackText.Text = $"{props.Title} — {props.Artist}";
                    SourceText.Text = "Looking for lyrics…"; LyricsText.Text = "♪";
                    var track = library.Match(props.Title ?? "", props.Artist ?? "", props.AlbumTitle ?? "", timeline.EndTime.TotalSeconds);
                    lyrics = await LyricsResolver.ResolveAsync(track, props.Title ?? "", props.Artist ?? "", ct);
                    SourceText.Text = lyrics is null ? "No synchronized lyrics found" : lyrics.Source;
                    continue; // Recheck track before showing resolved lyrics.
                }
                if (lyrics is null) LyricsText.Text = "♪";
                else
                {
                    var pos = timeline.Position.TotalSeconds;
                    var line = lyrics.Lines.LastOrDefault(l => l.Time <= pos);
                    LyricsText.Text = line?.Text ?? "♪";
                }
                await Task.Delay(250, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { LyricsText.Text = "Media monitoring stopped"; report("Media error: " + ex.Message); } }
    }
    private delegate bool EnumProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string name, string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
}
