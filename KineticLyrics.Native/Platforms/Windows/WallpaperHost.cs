using System.ComponentModel;
using System.Runtime.InteropServices;
using KineticLyrics.Native.Models;
using KineticLyrics.Native.Services;

namespace KineticLyrics.Native.Platforms;

/// <summary>
/// Hosts a transparent native Win32 surface in the desktop's wallpaper WorkerW layer.
/// The surface is intentionally separate from the small MAUI configuration window.
/// </summary>
public sealed class WallpaperHost : IWallpaperHost, IDisposable
{
    private const uint DesktopWorkerMessage = 0x052C;
    private const uint SmtoNormal = 0;
    private const uint WsPopup = 0x80000000;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint LwaColorKey = 0x00000001;
    private const int GwlStyle = -16;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int WmCreate = 0x0001;
    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmTimer = 0x0113;
    private const int TimerFrame = 1;
    private const int WmNccreate = 0x0081;
    private const int GwlpUserData = -21;
    private const uint DtCenter = 0x00000001;
    private const uint DtVCenter = 0x00000004;
    private const uint DtWordBreak = 0x00000010;
    private const uint DtNoClip = 0x00000100;
    private const int Transparent = 1;
    private const int FontBold = 700;
    private const int FontNormal = 400;
    private const int FontQualityClearType = 5;
    private const int ColorRed = 0x004D4DFF;
    private const int ColorWhite = 0x00EAF1F4;
    private const int ColorMuted = 0x007D7D94;

    private static readonly string WindowClass = "KineticLyrics.DesktopWallpaper.NativeSurface";
    private static readonly WindowProcedure WindowProc = DispatchWindowMessage;
    private static readonly MonitorEnumProcedure MonitorProc = CollectMonitor;
    private static bool _classRegistered;
    private static List<Rect>? _monitorBuffer;

    private readonly MediaSessionService _media;
    private readonly LyricsService _lyrics;
    private readonly object _stateLock = new();
    private readonly object _windowsLock = new();
    private readonly List<IntPtr> _windows = [];
    private readonly GCHandle _selfHandle;
    private CancellationTokenSource? _lyricsRequest;
    private CancellationTokenSource? _lifetime;
    private MediaTrackSnapshot? _track;
    private LyricsDocument? _document;
    private string _status = "Starting wallpaper renderer";
    private double _lyricsOffsetSeconds;
    private int _started;
    private IntPtr _workerWindow;

    public WallpaperHost(MediaSessionService media, LyricsService lyrics)
    {
        _media = media;
        _lyrics = lyrics;
        _lyricsOffsetSeconds = Math.Clamp(Preferences.Default.Get("lyrics_offset_seconds", 0d), -2, 2);
        _selfHandle = GCHandle.Alloc(this);
    }

    public event EventHandler<string>? StatusChanged;
    public string Status { get { lock (_stateLock) return _status; } }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        try
        {
            EnsureWindowClass();
            CreateWallpaperWindows();
            _media.TrackChanged += OnTrackChanged;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetStatus("Native desktop wallpaper surface created");
            await _media.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not attach wallpaper to the desktop: {ex.Message}");
        }
    }

    public async Task RefreshLyricsAsync(CancellationToken cancellationToken = default)
    {
        MediaTrackSnapshot? track;
        lock (_stateLock) track = _track;
        if (track is not null)
            await LoadLyricsAsync(track, cancellationToken).ConfigureAwait(false);
    }

    public void SetLyricsOffset(double seconds)
    {
        _lyricsOffsetSeconds = Math.Clamp(seconds, -2, 2);
        InvalidateWallpaperWindows();
    }

    private void OnTrackChanged(object? sender, MediaTrackSnapshot? track)
    {
        lock (_stateLock)
        {
            _track = track;
            _document = null;
            _lyricsRequest?.Cancel();
            _lyricsRequest?.Dispose();
            _lyricsRequest = track is null ? null : CancellationTokenSource.CreateLinkedTokenSource(_lifetime?.Token ?? CancellationToken.None);
        }
        if (track is null)
            SetStatus("Waiting for music");
        else
        {
            SetStatus($"Looking for lyrics: {track.Title}");
            CancellationToken token;
            lock (_stateLock) token = _lyricsRequest?.Token ?? CancellationToken.None;
            _ = LoadLyricsAsync(track, token);
        }
        InvalidateWallpaperWindows();
    }

    private async Task LoadLyricsAsync(MediaTrackSnapshot track, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _lyrics.FindAsync(track, cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
            {
                if (_track?.Identity != track.Identity || _track?.Source != track.Source)
                    return;
                _document = document;
            }
            SetStatus(document is null ? "No synced lyrics found" : $"Lyrics loaded from {document.Source}");
            InvalidateWallpaperWindows();
        }
        catch (OperationCanceledException) { }
        catch
        {
            SetStatus("Lyrics are unavailable");
        }
    }

    private void CreateWallpaperWindows()
    {
        lock (_windowsLock)
        {
            var progman = FindWindowW("Progman", null);
            if (progman == IntPtr.Zero)
                throw new InvalidOperationException("The Windows desktop host was not found.");

            _ = SendMessageTimeoutW(progman, DesktopWorkerMessage, new IntPtr(0xD), new IntPtr(1), SmtoNormal, 1000, out _);
            var worker = FindWallpaperWorker(progman);
            if (worker == IntPtr.Zero)
                worker = progman;
            var parentRect = new Rect();
            if (!GetWindowRect(worker, ref parentRect))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the desktop wallpaper host bounds.");
            _workerWindow = worker;

            var monitors = new List<Rect>();
            _monitorBuffer = monitors;
            _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorProc, IntPtr.Zero);
            _monitorBuffer = null;
            if (monitors.Count == 0)
                monitors.Add(new Rect { Left = 0, Top = 0, Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) });

            foreach (var monitor in monitors)
            {
                var x = monitor.Left - parentRect.Left;
                var y = monitor.Top - parentRect.Top;
                var width = Math.Max(1, monitor.Right - monitor.Left);
                var height = Math.Max(1, monitor.Bottom - monitor.Top);
                var hwnd = CreateWindowExW(
                    WsExLayered | WsExToolWindow,
                    WindowClass,
                    "Kinetic Lyrics Wallpaper",
                    WsPopup,
                    monitor.Left,
                    monitor.Top,
                    width,
                    height,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    GetModuleHandleW(null),
                    GCHandle.ToIntPtr(_selfHandle));
                if (hwnd == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW could not create the native wallpaper surface.");

                try
                {
                    var childStyle = new IntPtr(unchecked((int)(WsChild | WsClipSiblings)));
                    if (SetWindowLongPtr(hwnd, GwlStyle, childStyle) == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not prepare the wallpaper surface as a desktop child window.");

                    SetLastError(0);
                    var previousParent = SetParent(hwnd, worker);
                    var parentError = Marshal.GetLastWin32Error();
                    if (previousParent == IntPtr.Zero && parentError != 0)
                        throw new Win32Exception(parentError, "Could not attach the wallpaper surface to Explorer's WorkerW window.");

                    if (GetParent(hwnd) != worker)
                        throw new InvalidOperationException($"The wallpaper surface did not attach to the desktop host (WorkerW 0x{worker.ToInt64():X}).");

                    if (!SetLayeredWindowAttributes(hwnd, 0, 255, LwaColorKey))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not enable transparency for the wallpaper surface.");
                    if (!SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate | SwpShowWindow))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the wallpaper surface on its monitor.");

                    _windows.Add(hwnd);
                }
                catch
                {
                    if (IsWindow(hwnd))
                        _ = DestroyWindow(hwnd);
                    throw;
                }
            }
        }
    }

    private static IntPtr FindWallpaperWorker(IntPtr progman)
    {
        var found = IntPtr.Zero;
        _ = EnumWindows((top, _) =>
        {
            var shellView = FindWindowExW(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
                return true;
            found = FindWindowExW(IntPtr.Zero, top, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return found == IntPtr.Zero ? progman : found;
    }

    private void InvalidateWallpaperWindows()
    {
        lock (_windowsLock)
        {
            foreach (var hwnd in _windows)
                if (IsWindow(hwnd)) _ = InvalidateRect(hwnd, IntPtr.Zero, false);
        }
    }

    private void SetStatus(string message)
    {
        lock (_stateLock) _status = message;
        StatusChanged?.Invoke(this, message);
    }

    private void Paint(IntPtr hwnd)
    {
        var paint = new PaintStruct { Reserved = new byte[32] };
        var hdc = BeginPaint(hwnd, ref paint);
        if (hdc == IntPtr.Zero)
            return;
        try
        {
            _ = GetClientRect(hwnd, out var client);
            var width = Math.Max(1, client.Right - client.Left);
            var height = Math.Max(1, client.Bottom - client.Top);
            var black = CreateSolidBrush(0);
            _ = FillRect(hdc, ref client, black);
            _ = DeleteObject(black);
            _ = SetBkMode(hdc, Transparent);

            MediaTrackSnapshot? track;
            LyricsDocument? document;
            lock (_stateLock) (track, document) = (_track, _document);
            var offset = _lyricsOffsetSeconds;
            var position = track?.CurrentPositionSeconds(DateTimeOffset.UtcNow) ?? 0;
            var lineIndex = document?.CurrentLineIndex(Math.Max(0, position + offset)) ?? -1;
            var centerY = (int)(height * .53);
            var sideFont = Math.Clamp(height / 28, 16, 34);
            var mainFont = Math.Clamp(height / 13, 30, 92);
            var maxTextWidth = (int)(width * .78);

            if (track is null)
            {
                DrawTextLine(hdc, "KINETIC LYRICS", width / 2, height / 2, maxTextWidth, Math.Clamp(height / 20, 22, 64), ColorWhite, FontBold);
                DrawTextLine(hdc, "waiting for music", width / 2, height / 2 + sideFont * 2, maxTextWidth, sideFont, ColorMuted, FontNormal);
                return;
            }

            if (document is { Lines.Count: > 0 } && lineIndex >= 0)
            {
                if (lineIndex > 0)
                    DrawTextLine(hdc, document.Lines[lineIndex - 1].Text, width / 2, (int)(centerY - mainFont * 1.55), maxTextWidth, sideFont, ColorMuted, FontNormal);
                DrawTextLine(hdc, document.Lines[lineIndex].Text, width / 2, centerY, maxTextWidth, mainFont, ColorRed, FontBold);
                if (lineIndex + 1 < document.Lines.Count)
                    DrawTextLine(hdc, document.Lines[lineIndex + 1].Text, width / 2, (int)(centerY + mainFont * 1.65), maxTextWidth, sideFont, ColorWhite, FontNormal);
                var progress = Math.Clamp((position + offset - document.Lines[lineIndex].TimeSeconds) / 4.0, 0, 1);
                DrawProgress(hdc, width, (int)(height * .68), progress);
            }
            else
            {
                var notice = document is null ? "finding synced lyrics" : "";
                if (notice.Length > 0)
                    DrawTextLine(hdc, notice, width / 2, centerY, maxTextWidth, sideFont, ColorMuted, FontNormal);
            }

            DrawTextLine(hdc, track.Title, (int)(width * .04), (int)(height * .92), (int)(width * .75), Math.Clamp(height / 46, 13, 23), ColorWhite, FontBold, centered: false);
            DrawTextLine(hdc, track.Artist, (int)(width * .04), (int)(height * .96), (int)(width * .75), Math.Clamp(height / 58, 11, 19), ColorMuted, FontNormal, centered: false);
        }
        finally
        {
            EndPaint(hwnd, ref paint);
        }
    }

    private static void DrawTextLine(IntPtr hdc, string text, int x, int y, int width, int size, int color, int weight, bool centered = true)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var font = CreateFontW(-size, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, FontQualityClearType, 0, "Segoe UI");
        var oldFont = SelectObject(hdc, font);
        _ = SetTextColor(hdc, color);
        var rect = new Rect
        {
            Left = centered ? x - width / 2 : x,
            Top = y - size,
            Right = centered ? x + width / 2 : x + width,
            Bottom = y + size
        };
        var flags = DtVCenter | DtWordBreak | DtNoClip | (centered ? DtCenter : 0u);
        _ = DrawTextW(hdc, text, text.Length, ref rect, flags);
        _ = SelectObject(hdc, oldFont);
        _ = DeleteObject(font);
    }

    private static void DrawProgress(IntPtr hdc, int width, int y, double progress)
    {
        var x1 = (int)(width * .30);
        var x2 = (int)(width * .70);
        var pen = CreatePen(0, 3, ColorRed);
        var oldPen = SelectObject(hdc, pen);
        _ = MoveToEx(hdc, x1, y, IntPtr.Zero);
        _ = LineTo(hdc, x1 + (int)((x2 - x1) * progress), y);
        _ = SelectObject(hdc, oldPen);
        _ = DeleteObject(pen);
    }

    private static IntPtr DispatchWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmNccreate)
        {
            var create = Marshal.PtrToStructure<CreateStruct>(lParam);
            _ = SetWindowLongPtr(hwnd, GwlpUserData, create.CreateParams);
        }
        var data = GetWindowLongPtr(hwnd, GwlpUserData);
        var owner = data == IntPtr.Zero ? null : GCHandle.FromIntPtr(data).Target as WallpaperHost;
        switch (message)
        {
            case WmCreate:
                _ = SetTimer(hwnd, TimerFrame, 40, IntPtr.Zero);
                return IntPtr.Zero;
            case WmTimer:
                _ = InvalidateRect(hwnd, IntPtr.Zero, false);
                return IntPtr.Zero;
            case WmEraseBkgnd:
                return new IntPtr(1);
            case WmPaint:
                owner?.Paint(hwnd);
                return IntPtr.Zero;
            case WmDestroy:
                _ = KillTimer(hwnd, TimerFrame);
                return IntPtr.Zero;
            default:
                return DefWindowProcW(hwnd, message, wParam, lParam);
        }
    }

    private static bool CollectMonitor(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data)
    {
        var monitors = _monitorBuffer;
        if (monitors is null) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfoW(monitor, ref info))
            monitors.Add(info.Monitor);
        return true;
    }

    private static void EnsureWindowClass()
    {
        lock (typeof(WallpaperHost))
        {
            if (_classRegistered) return;
            var windowClass = new WindowClassEx
            {
                Size = (uint)Marshal.SizeOf<WindowClassEx>(),
                WindowProcedure = WindowProc,
                Instance = GetModuleHandleW(null),
                ClassName = WindowClass
            };
            if (RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new InvalidOperationException("Could not register the native wallpaper window class.");
            _classRegistered = true;
        }
    }

    public void Dispose()
    {
        _media.TrackChanged -= OnTrackChanged;
        _lyricsRequest?.Cancel();
        _lifetime?.Cancel();
        lock (_windowsLock)
        {
            foreach (var hwnd in _windows)
                if (IsWindow(hwnd)) _ = DestroyWindow(hwnd);
            _windows.Clear();
        }
        if (_selfHandle.IsAllocated) _selfHandle.Free();
        _lifetime?.Dispose();
        _lyricsRequest?.Dispose();
    }

    private delegate bool EnumWindowsProcedure(IntPtr hwnd, IntPtr lParam);
    private delegate bool MonitorEnumProcedure(IntPtr monitor, IntPtr hdc, IntPtr clip, IntPtr data);
    private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint Size;
        public uint Style;
        public WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string? ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public IntPtr CreateParams;
        public IntPtr Instance;
        public IntPtr Menu;
        public IntPtr Parent;
        public int Height;
        public int Width;
        public int Y;
        public int X;
        public int Style;
        public IntPtr Name;
        public IntPtr Class;
        public uint ExtendedStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public Rect Monitor; public Rect Work; public uint Flags; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindWindowW(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(EnumWindowsProcedure callback, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProcedure callback, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, ref Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, int colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr BeginPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EndPaint(IntPtr hwnd, ref PaintStruct paint);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SetTimer(IntPtr hwnd, nuint id, uint interval, IntPtr callback);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool KillTimer(IntPtr hwnd, nuint id);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string? moduleName);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern void SetLastError(uint error);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WindowClassEx windowClass);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
    [DllImport("gdi32.dll")] private static extern IntPtr CreatePen(int style, int width, int color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr hdc, int color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr hdc, string text, int length, ref Rect rect, uint format);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);
    [DllImport("gdi32.dll")] private static extern bool MoveToEx(IntPtr hdc, int x, int y, IntPtr previousPoint);
    [DllImport("gdi32.dll")] private static extern bool LineTo(IntPtr hdc, int x, int y);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
}




