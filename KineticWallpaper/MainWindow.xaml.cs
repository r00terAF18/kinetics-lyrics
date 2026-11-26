using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KineticLyrics;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;

namespace KineticWallpaper;

public partial class MainWindow : Window
{
    private const string BridgeScript = """
(function() {
  const listeners = {
    status: [],
    props: [],
    thumb: [],
    playback: [],
    timeline: [],
    audio: []
  };
  const latest = {};
  const registerLatest = (channel, fn) => {
    if (typeof fn !== 'function') return;
    listeners[channel].push(fn);
    if (latest[channel]) { try { fn(latest[channel]); } catch (e) {} }
  };

  window.wallpaperMediaIntegration = {
    PLAYBACK_PLAYING: 1,
    PLAYBACK_PAUSED: 2,
    PLAYBACK_STOPPED: 3
  };

  window.__klLocalLrc = null;
  window.__klTargetFps = 60;

  // Frame-rate limiter to keep GPU/DWM load minimal
  const origRaf = window.requestAnimationFrame.bind(window);
  let lastRaf = 0;
  window.requestAnimationFrame = function(cb) {
    const fps = window.__klTargetFps || 0;
    if (fps <= 0 || fps >= 60) return origRaf(cb);
    const minInterval = 1000 / fps;
    return origRaf(function(ts) {
      if (ts - lastRaf >= minInterval * 0.85) {
        lastRaf = ts;
        cb(ts);
      } else {
        window.requestAnimationFrame(cb);
      }
    });
  };

  window.wallpaperRegisterMediaStatusListener = fn => { if (typeof fn === 'function') { listeners.status.push(fn); fn({ enabled: true }); } };
  window.wallpaperRegisterMediaPropertiesListener = fn => registerLatest('props', fn);
  window.wallpaperRegisterMediaThumbnailListener = fn => registerLatest('thumb', fn);
  window.wallpaperRegisterMediaPlaybackListener = fn => registerLatest('playback', fn);
  window.wallpaperRegisterMediaTimelineListener = fn => registerLatest('timeline', fn);
  window.wallpaperRegisterAudioListener = fn => { if (typeof fn === 'function') listeners.audio.push(fn); };

  // Prioritize local embedded tags / .lrc sidecar files before online LRCLIB/NetEase lookup
  window.addEventListener('DOMContentLoaded', () => {
    if (typeof window.fetchLyrics === 'function') {
      const origFetchLyrics = window.fetchLyrics;
      window.fetchLyrics = async function() {
        if (window.__klLocalLrc && window.__klLocalLrc.lrc) {
          ++S.reqId;
          const key = S.artist + '|' + S.title;
          S.lines = [];
          S.idx = -99;
          resetSync(key);
          const lines = parseLRC(window.__klLocalLrc.lrc);
          if (lines.length > 2) {
            S.src = window.__klLocalLrc.source + (lines.some(l => l.wt) ? ' · word-synced' : '');
            S.lines = prepLines(lines);
            S.idx = -99;
            status(S.src);
            return;
          }
        }
        return origFetchLyrics.apply(this, arguments);
      };
    }
  });

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', ev => {
      const msg = ev.data;
      if (!msg || typeof msg !== 'object') return;
      switch (msg.type) {
        case 'localLyrics':
          window.__klLocalLrc = msg.data;
          if (msg.data && msg.data.lrc && typeof window.fetchLyrics === 'function') {
            window.fetchLyrics();
          }
          break;
        case 'fps':
          window.__klTargetFps = msg.data || 0;
          break;
        case 'props':
          latest.props = msg.data;
          window.__klLocalLrc = null;
          listeners.props.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'thumb':
          latest.thumb = msg.data;
          listeners.thumb.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'playback':
          latest.playback = msg.data;
          listeners.playback.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'timeline':
          latest.timeline = msg.data;
          listeners.timeline.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'audio':
          listeners.audio.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'settings':
          if (window.wallpaperPropertyListener && window.wallpaperPropertyListener.applyUserProperties) {
            try { window.wallpaperPropertyListener.applyUserProperties(msg.data); } catch (e) {}
          }
          break;
      }
    });
  }
})();
""";

    private readonly CancellationTokenSource _cts = new();
    private readonly MusicLibrary? _library;
    private readonly MediaMonitor _monitor;
    private readonly AudioReactor _audio = new();
    private readonly WallpaperSettings _settings;
    private readonly DispatcherTimer _bridgeTimer;
    private Forms.NotifyIcon? _trayIcon;
    private HudMouseForwarder? _hudMouseForwarder;
    private SettingsWindow? _settingsWindow;
    private Action? _refreshMonitorMenu;
    private Action? _refreshDisplayModeMenu;

    private bool _webReady;
    private string _lastTrackSig = "";
    private string? _lastSentLocalLrc;
    private byte[]? _lastThumbBytes;
    private bool? _lastPlayingState;
    private double _lastSentPos = -1;
    private double _lastSentDur = -1;
    private int _tickCounter;

    public long RenderFrameCount { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        _settings = WallpaperSettings.Load();
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
        Directory.CreateDirectory(dataDir);

        try
        {
            _library = new MusicLibrary(Path.Combine(dataDir, "library.db"));
            if (_settings.MusicFolders.Count == 0)
            {
                var defaultMusic = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
                if (Directory.Exists(defaultMusic))
                {
                    _settings.MusicFolders.Add(defaultMusic);
                    _settings.Save();
                }
            }
            if (_settings.MusicFolders.Count > 0)
            {
                var folders = _settings.MusicFolders.ToList();
                _ = Task.Run(() =>
                {
                    foreach (var folder in folders)
                        if (Directory.Exists(folder)) _library.Scan(folder);
                });
            }
        }
        catch (Exception ex) { App.LogCrash("MusicLibrary", ex); }

        _monitor = new MediaMonitor(_library);

        SetupTrayIcon();

        _bridgeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33) // 30 Hz bridge tick
        };
        _bridgeTimer.Tick += OnBridgeTick;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KineticLyrics", "WebView2Data");
            Directory.CreateDirectory(userDataFolder);

            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            await WebView.EnsureCoreWebView2Async(env);

            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

            _hudMouseForwarder = new HudMouseForwarder(WebView.CoreWebView2, DesktopWallpaperHost.GetTargetScreenRect);
            WebView.CoreWebView2.WebMessageReceived += (_, args) =>
            {
                try
                {
                    using var message = JsonDocument.Parse(args.WebMessageAsJson);
                    var root = message.RootElement;
                    if (root.TryGetProperty("type", out var type) && type.GetString() == "hudBounds" &&
                        root.TryGetProperty("data", out var data))
                        _hudMouseForwarder?.UpdateBounds(data);
                }
                catch (JsonException) { }
            };

            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);

            var webContentDir = Path.Combine(AppContext.BaseDirectory, "WebContent");
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "kineticlyrics.local",
                webContentDir,
                CoreWebView2HostResourceAccessKind.Allow);

            WebView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    _webReady = true;
                    UpdateHudMouseForwarding();
                    PostWebMessage("fps", _settings.TargetFps);
                    PushSettings();
                }
            };

            WebView.CoreWebView2.Navigate("https://kineticlyrics.local/index.html");

            DesktopWallpaperHost.TargetMonitorDeviceName = _settings.ResolveTargetDeviceName();
            DesktopWallpaperHost.Attach(this, ParseDisplayMode(_settings.DisplayMode));
            UpdateHudMouseForwarding();

            _audio.Start();
            _ = Task.Run(() => _monitor.RunAsync(_cts.Token));
            _bridgeTimer.Start();
        }
        catch (Exception ex)
        {
            App.LogCrash("OnLoaded", ex);
            MessageBox.Show($"Failed to initialize WebView2 wallpaper:\n\n{ex.Message}", "Kinetic Wallpaper", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnBridgeTick(object? sender, EventArgs e)
    {
        RenderFrameCount++;
        if (!_webReady || WebView.CoreWebView2 is null) return;

        var snap = _monitor.GetSnapshot();

        if (snap.IsPlaying || (_tickCounter++ % 4 == 0))
        {
            var bins = _audio.GetFftBinsCopy();
            PostWebMessage("audio", bins);
        }

        string trackSig = $"{snap.Title}|{snap.Artist}|{snap.Album}";
        if (trackSig != _lastTrackSig)
        {
            _lastTrackSig = trackSig;
            _lastSentLocalLrc = null;
            _lastSentPos = -1;
            _lastSentDur = -1;
            PostWebMessage("props", new
            {
                title = snap.Title,
                artist = snap.Artist,
                albumTitle = snap.Album
            });
        }

        if (snap.LocalRawLrc is not null && snap.LocalRawLrc != _lastSentLocalLrc)
        {
            _lastSentLocalLrc = snap.LocalRawLrc;
            PostWebMessage("localLyrics", new
            {
                source = snap.LocalSource ?? "Local LRC",
                lrc = snap.LocalRawLrc
            });
        }

        if (!ReferenceEquals(snap.ThumbnailBytes, _lastThumbBytes))
        {
            _lastThumbBytes = snap.ThumbnailBytes;
            string dataUrl = snap.ThumbnailBytes is { Length: > 0 }
                ? $"data:image/jpeg;base64,{Convert.ToBase64String(snap.ThumbnailBytes)}"
                : "";
            PostWebMessage("thumb", new { thumbnail = dataUrl });
        }

        if (_lastPlayingState != snap.IsPlaying)
        {
            _lastPlayingState = snap.IsPlaying;
            PostWebMessage("playback", new { state = snap.IsPlaying ? 1 : 2 });
        }

        if (snap.IsPlaying && (Math.Abs(snap.InterpolatedPosition - _lastSentPos) > 0.12 || Math.Abs(snap.Duration - _lastSentDur) > 0.5))
        {
            _lastSentPos = snap.InterpolatedPosition;
            _lastSentDur = snap.Duration;
            PostWebMessage("timeline", new
            {
                position = snap.InterpolatedPosition,
                duration = snap.Duration
            });
        }
    }

    private void PushSettings()
    {
        if (!_webReady || WebView.CoreWebView2 is null) return;
        PostWebMessage("settings", _settings.ToWallpaperEngineProperties());
    }

    private void PostWebMessage(string type, object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { type, data });
            WebView.CoreWebView2?.PostWebMessageAsJson(json);
        }
        catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settingsWindow?.Close();
        _hudMouseForwarder?.Dispose();
        _bridgeTimer.Stop();
        _cts.Cancel();
        _audio.Dispose();
        _library?.Dispose();
        _trayIcon?.Dispose();
    }

    private void UpdateHudMouseForwarding()
    {
        _hudMouseForwarder?.SetEnabled(
            _webReady && DesktopWallpaperHost.CurrentMode != DesktopAttachMode.FloatingWindow);
    }

    private static DesktopAttachMode ParseDisplayMode(string? mode) => mode switch
    {
        "bottom" => DesktopAttachMode.BottomMostWindow,
        "floating" => DesktopAttachMode.FloatingWindow,
        _ => DesktopAttachMode.AutoShell
    };

    private static string ToDisplayMode(DesktopAttachMode mode) => mode switch
    {
        DesktopAttachMode.BottomMostWindow => "bottom",
        DesktopAttachMode.FloatingWindow => "floating",
        _ => "auto"
    };

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            if (!_settingsWindow.IsVisible)
                _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, ApplySettingsFromWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void ApplySettingsFromWindow(IReadOnlyList<string> addedFolders, IReadOnlyList<string> removedFolders)
    {
        DesktopWallpaperHost.TargetMonitorDeviceName = _settings.ResolveTargetDeviceName();
        DesktopWallpaperHost.SetMode(this, ParseDisplayMode(_settings.DisplayMode));
        UpdateHudMouseForwarding();
        _refreshMonitorMenu?.Invoke();
        _refreshDisplayModeMenu?.Invoke();
        PostWebMessage("fps", _settings.TargetFps);
        PushSettings();

        var library = _library;
        if (library is not null && (addedFolders.Count > 0 || removedFolders.Count > 0))
        {
            var foldersToScan = _settings.MusicFolders.ToArray();
            _ = Task.Run(() =>
            {
                foreach (var folder in removedFolders)
                {
                    try { library.RemoveFolder(folder); }
                    catch (Exception ex) { App.LogCrash("RemoveMusicFolder", ex); }
                }
                foreach (var folder in foldersToScan.Where(Directory.Exists))
                {
                    try { library.Scan(folder); }
                    catch (Exception ex) { App.LogCrash("ScanMusicFolder", ex); }
                }
            });
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Kinetic Lyrics Wallpaper",
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettingsWindow);

        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("Settings…", null, (_, _) => Dispatcher.Invoke(OpenSettingsWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());

        // Monitor Selection submenu
        var monitorMenu = new Forms.ToolStripMenuItem("Monitor");
        void RebuildMonitorMenu()
        {
            monitorMenu.DropDownItems.Clear();

            var primaryItem = new Forms.ToolStripMenuItem("Primary Monitor")
            {
                Checked = string.Equals(_settings.TargetMonitor, "PRIMARY", StringComparison.OrdinalIgnoreCase)
            };
            primaryItem.Click += (_, _) => SelectMonitor("PRIMARY");
            monitorMenu.DropDownItems.Add(primaryItem);

            var screens = Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var sc = screens[i];
                string dev = sc.DeviceName;
                string label = $"Display {i + 1} ({sc.Bounds.Width}x{sc.Bounds.Height}){(sc.Primary ? " [Primary]" : "")}";
                var item = new Forms.ToolStripMenuItem(label)
                {
                    Checked = string.Equals(_settings.TargetMonitor, dev, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += (_, _) => SelectMonitor(dev);
                monitorMenu.DropDownItems.Add(item);
            }

            if (screens.Length > 1)
            {
                monitorMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
                var allItem = new Forms.ToolStripMenuItem("Span All Monitors")
                {
                    Checked = string.Equals(_settings.TargetMonitor, "ALL", StringComparison.OrdinalIgnoreCase)
                };
                allItem.Click += (_, _) => SelectMonitor("ALL");
                monitorMenu.DropDownItems.Add(allItem);
            }
        }

        _refreshMonitorMenu = RebuildMonitorMenu;

        void SelectMonitor(string target)
        {
            _settings.TargetMonitor = target;
            _settings.Save();
            RebuildMonitorMenu();
            Dispatcher.Invoke(() =>
            {
                DesktopWallpaperHost.TargetMonitorDeviceName = _settings.ResolveTargetDeviceName();
                DesktopWallpaperHost.SetMode(this, DesktopWallpaperHost.CurrentMode);
                PushSettings();
            });
        }

        RebuildMonitorMenu();
        menu.Items.Add(monitorMenu);

        // Display Mode submenu
        var modeMenu = new Forms.ToolStripMenuItem("Display Mode");
        var modeAuto = new Forms.ToolStripMenuItem("Behind Desktop Icons (Auto)") { Checked = _settings.DisplayMode == "auto" };
        var modeBottom = new Forms.ToolStripMenuItem("Bottom-Most Desktop Window") { Checked = _settings.DisplayMode == "bottom" };
        var modeFloat = new Forms.ToolStripMenuItem("Floating Window (Preview)") { Checked = _settings.DisplayMode == "floating" };

        void RefreshDisplayModeMenu()
        {
            modeAuto.Checked = _settings.DisplayMode == "auto";
            modeBottom.Checked = _settings.DisplayMode == "bottom";
            modeFloat.Checked = _settings.DisplayMode == "floating";
        }

        _refreshDisplayModeMenu = RefreshDisplayModeMenu;

        void SelectMode(DesktopAttachMode m)
        {
            _settings.DisplayMode = ToDisplayMode(m);
            _settings.Save();
            RefreshDisplayModeMenu();
            Dispatcher.Invoke(() =>
            {
                DesktopWallpaperHost.SetMode(this, m);
                UpdateHudMouseForwarding();
            });
        }

        modeAuto.Click += (_, _) => SelectMode(DesktopAttachMode.AutoShell);
        modeBottom.Click += (_, _) => SelectMode(DesktopAttachMode.BottomMostWindow);
        modeFloat.Click += (_, _) => SelectMode(DesktopAttachMode.FloatingWindow);
        modeMenu.DropDownItems.AddRange([modeAuto, modeBottom, modeFloat]);
        menu.Items.Add(modeMenu);

        // Local Music Library submenu
        var musicMenu = new Forms.ToolStripMenuItem("Local Music Folders");
        musicMenu.DropDownItems.Add("Add Music Folder…", null, (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "Select music folder for embedded tags & .lrc files" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                if (!_settings.MusicFolders.Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
                {
                    _settings.MusicFolders.Add(dialog.SelectedPath);
                    _settings.Save();
                }
                if (_library is not null)
                    _ = Task.Run(() => _library.Scan(dialog.SelectedPath));
            }
        });
        musicMenu.DropDownItems.Add("Rescan Indexed Folders", null, (_, _) =>
        {
            if (_library is null) return;
            var folders = _settings.MusicFolders.ToList();
            _ = Task.Run(() =>
            {
                foreach (var f in folders)
                    if (Directory.Exists(f)) _library.Scan(f);
            });
        });
        menu.Items.Add(musicMenu);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Camera Mode submenu
        var camMenu = new Forms.ToolStripMenuItem("Camera Mode");
        foreach (var (label, val) in new[]
        {
            ("Auto (Follow + Stream)", "auto"),
            ("Follow Canvas + Letter Dive", "follow"),
            ("Stream (Continuous Path)", "stream"),
            ("Edit Moves (Whips & Spins)", "tiktok"),
            ("Edit Moves (Subtle)", "subtle"),
            ("Off", "off")
        })
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = _settings.Camera == val };
            item.Click += (_, _) =>
            {
                _settings.Camera = val;
                _settings.Save();
                foreach (Forms.ToolStripMenuItem sibling in camMenu.DropDownItems) sibling.Checked = false;
                item.Checked = true;
                PushSettings();
            };
            camMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(camMenu);

        // Background Mode submenu
        var bgMenu = new Forms.ToolStripMenuItem("Background");
        foreach (var (label, val) in new[]
        {
            ("Album Art (Blurred)", "art"),
            ("Album Colour Gradient", "gradient"),
            ("Solid Black", "solid"),
            ("Animated — Flowing Colours", "anim"),
            ("Animated — Drifting Art", "drift")
        })
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = _settings.BgMode == val };
            item.Click += (_, _) =>
            {
                _settings.BgMode = val;
                _settings.Save();
                foreach (Forms.ToolStripMenuItem sibling in bgMenu.DropDownItems) sibling.Checked = false;
                item.Checked = true;
                PushSettings();
            };
            bgMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(bgMenu);

        // Style Preset submenu
        var presetMenu = new Forms.ToolStripMenuItem("Style Preset");
        foreach (var (label, val) in new[]
        {
            ("Balanced", "balanced"),
            ("Minimal", "minimal"),
            ("Calm", "calm"),
            ("Hype", "hype"),
            ("Chaos", "chaos"),
            ("Synthwave", "synthwave"),
            ("Cyberpunk", "cyberpunk")
        })
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = _settings.Preset == val };
            item.Click += (_, _) =>
            {
                _settings.Preset = val;
                _settings.Save();
                foreach (Forms.ToolStripMenuItem sibling in presetMenu.DropDownItems) sibling.Checked = false;
                item.Checked = true;
                PushSettings();
            };
            presetMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(presetMenu);

        // Performance / FPS submenu
        var perfMenu = new Forms.ToolStripMenuItem("Performance");
        foreach (var (label, fps) in new[]
        {
            ("60 FPS (GPU Accelerated, Recommended)", 60),
            ("30 FPS (Low GPU)", 30),
            ("24 FPS (Battery Saver)", 24)
        })
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = _settings.TargetFps == fps };
            item.Click += (_, _) =>
            {
                _settings.TargetFps = fps;
                _settings.Save();
                foreach (var sibling in perfMenu.DropDownItems.OfType<Forms.ToolStripMenuItem>()) sibling.Checked = false;
                item.Checked = true;
                PostWebMessage("fps", fps);
            };
            perfMenu.DropDownItems.Add(item);
        }
        var lowPowerItem = new Forms.ToolStripMenuItem("Low-Power Animations") { Checked = _settings.LowPower, CheckOnClick = true };
        lowPowerItem.CheckedChanged += (_, _) => { _settings.LowPower = lowPowerItem.Checked; _settings.Save(); PushSettings(); };
        perfMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
        perfMenu.DropDownItems.Add(lowPowerItem);
        menu.Items.Add(perfMenu);

        menu.Items.Add(new Forms.ToolStripSeparator());

        // Toggles
        var clockItem = new Forms.ToolStripMenuItem("Idle Clock") { Checked = _settings.IdleClock, CheckOnClick = true };
        clockItem.CheckedChanged += (_, _) => { _settings.IdleClock = clockItem.Checked; _settings.Save(); PushSettings(); };
        menu.Items.Add(clockItem);

        var choreoItem = new Forms.ToolStripMenuItem("Song Choreography (Verse/Chorus)") { Checked = _settings.Choreo, CheckOnClick = true };
        choreoItem.CheckedChanged += (_, _) => { _settings.Choreo = choreoItem.Checked; _settings.Save(); PushSettings(); };
        menu.Items.Add(choreoItem);

        var infoItem = new Forms.ToolStripMenuItem("Show Song Info HUD") { Checked = _settings.ShowInfo, CheckOnClick = true };
        infoItem.CheckedChanged += (_, _) => { _settings.ShowInfo = infoItem.Checked; _settings.Save(); PushSettings(); };
        menu.Items.Add(infoItem);

        var invertItem = new Forms.ToolStripMenuItem("Invert Colours (Transparent Display)") { Checked = _settings.Invert, CheckOnClick = true };
        invertItem.CheckedChanged += (_, _) => { _settings.Invert = invertItem.Checked; _settings.Save(); PushSettings(); };
        menu.Items.Add(invertItem);

        // Sync Offset submenu
        var syncMenu = new Forms.ToolStripMenuItem("Lyrics Timing Offset");
        syncMenu.DropDownItems.Add("Show Earlier (+100ms)", null, (_, _) => { _settings.Offset += 100; _settings.Save(); PushSettings(); });
        syncMenu.DropDownItems.Add("Show Later (-100ms)", null, (_, _) => { _settings.Offset -= 100; _settings.Save(); PushSettings(); });
        syncMenu.DropDownItems.Add("Reset Offset (0ms)", null, (_, _) => { _settings.Offset = 0; _settings.Save(); PushSettings(); });
        menu.Items.Add(syncMenu);

        menu.Items.Add(new Forms.ToolStripSeparator());

        var startupItem = new Forms.ToolStripMenuItem("Start with Windows") { Checked = WallpaperSettings.IsStartupEnabled(), CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) => WallpaperSettings.SetStartupEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);

        menu.Items.Add("Show Desktop Diagnostics…", null, (_, _) =>
        {
            var info = Dispatcher.Invoke(() => DesktopWallpaperHost.GetDiagnostics(this));
            MessageBox.Show(info, "Kinetic Wallpaper Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
        });

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Wallpaper", null, (_, _) => Dispatcher.Invoke(Close));
        _trayIcon.ContextMenuStrip = menu;
    }
}
