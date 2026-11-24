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

  window.wallpaperMediaIntegration = {
    PLAYBACK_PLAYING: 1,
    PLAYBACK_PAUSED: 2,
    PLAYBACK_STOPPED: 3
  };

  window.wallpaperRegisterMediaStatusListener = fn => { if (typeof fn === 'function') { listeners.status.push(fn); fn({ enabled: true }); } };
  window.wallpaperRegisterMediaPropertiesListener = fn => { if (typeof fn === 'function') listeners.props.push(fn); };
  window.wallpaperRegisterMediaThumbnailListener = fn => { if (typeof fn === 'function') listeners.thumb.push(fn); };
  window.wallpaperRegisterMediaPlaybackListener = fn => { if (typeof fn === 'function') listeners.playback.push(fn); };
  window.wallpaperRegisterMediaTimelineListener = fn => { if (typeof fn === 'function') listeners.timeline.push(fn); };
  window.wallpaperRegisterAudioListener = fn => { if (typeof fn === 'function') listeners.audio.push(fn); };

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', ev => {
      const msg = ev.data;
      if (!msg || typeof msg !== 'object') return;
      switch (msg.type) {
        case 'props':
          listeners.props.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'thumb':
          listeners.thumb.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'playback':
          listeners.playback.forEach(fn => { try { fn(msg.data); } catch (e) {} });
          break;
        case 'timeline':
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

    private bool _webReady;
    private string _lastTrackSig = "";
    private byte[]? _lastThumbBytes;
    private bool? _lastPlayingState;
    private double _lastSentPos = -1;
    private double _lastSentDur = -1;

    public long RenderFrameCount { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        _settings = WallpaperSettings.Load();
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
        Directory.CreateDirectory(dataDir);

        try { _library = new MusicLibrary(Path.Combine(dataDir, "library.db")); }
        catch (Exception ex) { App.LogCrash("MusicLibrary", ex); }

        _monitor = new MediaMonitor(_library);

        SetupTrayIcon();

        _bridgeTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60 Hz audio + timeline feed
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

            // Inject the Wallpaper Engine listener APIs before index.html executes
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
                    PushSettings();
                }
            };

            WebView.CoreWebView2.Navigate("https://kineticlyrics.local/index.html");

            // Embed into the Windows desktop layer (uses the proven WebView2 WorkerW host)
            DesktopWallpaperHost.Attach(this, DesktopAttachMode.AutoShell);

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

        // 1. Push 128-bin WASAPI FFT spectrum for beat, kick, drop, and vocal glow
        var bins = _audio.GetFftBinsCopy();
        PostWebMessage("audio", bins);

        // 2. Push SMTC media properties, thumbnail, playback state, and timeline
        var snap = _monitor.GetSnapshot();

        string trackSig = $"{snap.Title}|{snap.Artist}|{snap.Album}";
        if (trackSig != _lastTrackSig)
        {
            _lastTrackSig = trackSig;
            _lastSentPos = -1;
            _lastSentDur = -1;
            PostWebMessage("props", new
            {
                title = snap.Title,
                artist = snap.Artist,
                albumTitle = snap.Album
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

        if (snap.IsPlaying && (Math.Abs(snap.InterpolatedPosition - _lastSentPos) > 0.04 || Math.Abs(snap.Duration - _lastSentDur) > 0.5))
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
        _bridgeTimer.Stop();
        _cts.Cancel();
        _audio.Dispose();
        _library?.Dispose();
        _trayIcon?.Dispose();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Kinetic Lyrics Wallpaper (WebView2)",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();

        // Display Mode submenu
        var modeMenu = new Forms.ToolStripMenuItem("Display Mode");
        var modeAuto = new Forms.ToolStripMenuItem("Behind Desktop Icons (Auto)") { Checked = true };
        var modeBottom = new Forms.ToolStripMenuItem("Bottom-Most Desktop Window");
        var modeFloat = new Forms.ToolStripMenuItem("Floating Window (Preview)");

        void SelectMode(DesktopAttachMode m)
        {
            modeAuto.Checked = m == DesktopAttachMode.AutoShell;
            modeBottom.Checked = m == DesktopAttachMode.BottomMostWindow;
            modeFloat.Checked = m == DesktopAttachMode.FloatingWindow;
            Dispatcher.Invoke(() => DesktopWallpaperHost.SetMode(this, m));
        }

        modeAuto.Click += (_, _) => SelectMode(DesktopAttachMode.AutoShell);
        modeBottom.Click += (_, _) => SelectMode(DesktopAttachMode.BottomMostWindow);
        modeFloat.Click += (_, _) => SelectMode(DesktopAttachMode.FloatingWindow);
        modeMenu.DropDownItems.AddRange([modeAuto, modeBottom, modeFloat]);
        menu.Items.Add(modeMenu);

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
                foreach (Forms.ToolStripMenuItem sibling in camMenu.DropDownItems)
                    sibling.Checked = false;
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
                foreach (Forms.ToolStripMenuItem sibling in bgMenu.DropDownItems)
                    sibling.Checked = false;
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
            ("Chaos", "chaos")
        })
        {
            var item = new Forms.ToolStripMenuItem(label) { Checked = _settings.Preset == val };
            item.Click += (_, _) =>
            {
                _settings.Preset = val;
                _settings.Save();
                foreach (Forms.ToolStripMenuItem sibling in presetMenu.DropDownItems)
                    sibling.Checked = false;
                item.Checked = true;
                PushSettings();
            };
            presetMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(presetMenu);

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

        menu.Items.Add("Open WebView2 DevTools", null, (_, _) => Dispatcher.Invoke(() => WebView.CoreWebView2?.OpenDevToolsWindow()));

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
