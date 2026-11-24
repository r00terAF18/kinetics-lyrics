using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using KineticLyrics;
using Forms = System.Windows.Forms;

namespace KineticWallpaper;

public partial class MainWindow : Window
{
    private sealed record ActiveWord(TextBlock Block, LyricWord Word, bool IsAccent, bool IsOutline, bool Triggered);
    private sealed class ActiveLineGroup
    {
        public required Canvas Container { get; init; }
        public required List<ActiveWord> Words { get; init; }
        public TextBlock? HotWord { get; set; }
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly MusicLibrary? _library;
    private readonly MediaMonitor _monitor;
    private readonly AudioReactor _audio = new();
    private readonly WallpaperSettings _settings;
    private Forms.NotifyIcon? _trayIcon;

    private Color _accentColor = Color.FromRgb(255, 77, 109);
    private byte[]? _lastThumbBytes;
    private string _trackKey = "";
    private int _currentLineIdx = -99;
    private bool _isClockMode;
    private string _clockKey = "";
    private long _lastActiveTicks = Stopwatch.GetTimestamp();
    private double _songNudgeSeconds;

    private LayoutKind? _lastLayout;
    private TransitionKind? _lastTransition;
    private ActiveLineGroup? _activeGroup;

    public MainWindow()
    {
        InitializeComponent();

        _settings = WallpaperSettings.Load();
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KineticLyrics");
        Directory.CreateDirectory(dataDir);

        try { _library = new MusicLibrary(Path.Combine(dataDir, "library.db")); } catch (Exception ex) { App.LogCrash("MusicLibrary", ex); }
        _monitor = new MediaMonitor(_library);

        SetupTrayIcon();
        ApplySettingsToUi();

        // Attach after Loaded so WPF's DirectX surface and window bounds are initialized
        SizeChanged += (_, _) => ApplySettingsToUi();
        Loaded += (_, _) =>
        {
            DesktopWallpaperHost.Attach(this);
            _audio.Start();
            _ = Task.Run(() => _monitor.RunAsync(_cts.Token));
            UpdateIdleClock();
            CompositionTarget.Rendering += OnRenderFrame;
        };
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRenderFrame;
            _cts.Cancel();
            _audio.Dispose();
            _library?.Dispose();
            _trayIcon?.Dispose();
        };
    }

    internal long RenderFrameCount { get; private set; }

    private void OnRenderFrame(object? sender, EventArgs e)
    {
        RenderFrameCount++;
        if (ActualWidth < 100 || ActualHeight < 100) return;

        _audio.StepFrame();
        double react = _settings.BeatReactivity;

        double t = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        double beatScale = 1.0 + _audio.BeatSmooth * 0.015 * react + _audio.KickImpulse * 0.025 * react + Math.Sin(t * 0.35) * 0.008;
        CameraScale.ScaleX = beatScale;
        CameraScale.ScaleY = beatScale;
        CameraRotate.Angle = Math.Sin(t * 0.22) * 0.65;

        double lightScale = 1.0 + _audio.BeatSmooth * 0.35 * react + _audio.DropImpulse * 0.6 * react;
        LightScale.ScaleX = lightScale;
        LightScale.ScaleY = lightScale;
        AmbientLight.Opacity = Math.Clamp(0.16 + _audio.Low * 0.28 * react + _audio.KickImpulse * 0.25 * react, 0.08, 0.85);
        Canvas.SetLeft(AmbientLight, (ActualWidth - AmbientLight.Width) / 2.0);
        Canvas.SetTop(AmbientLight, (ActualHeight - AmbientLight.Height) / 2.0);

        var snap = _monitor.GetSnapshot();
        if (snap.IsPlaying && !string.IsNullOrWhiteSpace(snap.Title))
            _lastActiveTicks = Stopwatch.GetTimestamp();

        if (!ReferenceEquals(snap.ThumbnailBytes, _lastThumbBytes))
        {
            _lastThumbBytes = snap.ThumbnailBytes;
            UpdateAlbumArtAndAccent(snap.ThumbnailBytes);
        }

        HudTitle.Text = snap.Title;
        HudArtist.Text = snap.Artist;
        StatusText.Text = $"{snap.StatusText.ToUpperInvariant()}  ·  {DesktopWallpaperHost.ModeDescription.ToUpperInvariant()}";
        if (snap.Duration > 0)
        {
            double pct = Math.Clamp(snap.InterpolatedPosition / snap.Duration, 0, 1);
            HudProgress.Width = 200 * pct;
        }

        bool shouldClock = _settings.ShowIdleClock &&
                           (!snap.IsPlaying || string.IsNullOrWhiteSpace(snap.Title)) &&
                           Stopwatch.GetElapsedTime(_lastActiveTicks).TotalSeconds > 3.0;

        if (shouldClock)
        {
            if (!_isClockMode)
            {
                _isClockMode = true;
                HudCard.Visibility = Visibility.Collapsed;
                RetireActiveGroup();
            }
            UpdateIdleClock();
            return;
        }

        if (_isClockMode)
        {
            _isClockMode = false;
            _clockKey = "";
            _currentLineIdx = -99;
            HudCard.Visibility = _settings.ShowSongInfo ? Visibility.Visible : Visibility.Collapsed;
        }

        string newTrackKey = $"{snap.Title}|{snap.Artist}";
        if (newTrackKey != _trackKey)
        {
            _trackKey = newTrackKey;
            _currentLineIdx = -99;
            _songNudgeSeconds = 0;
            UpdateSyncLabel();
        }

        double pos = snap.InterpolatedPosition + (_settings.GlobalOffsetMs / 1000.0) + _songNudgeSeconds;

        if (snap.Lyrics is null || snap.Lyrics.Lines.Count == 0)
        {
            if (_currentLineIdx != -1 && !string.IsNullOrWhiteSpace(snap.Title))
            {
                _currentLineIdx = -1;
                var titleLine = new LyricLine(0, 999, snap.Title, [new LyricWord(snap.Title, 0, 999)]);
                RenderLine(titleLine);
            }
            return;
        }

        int lineIdx = -1;
        for (int i = 0; i < snap.Lyrics.Lines.Count; i++)
        {
            if (snap.Lyrics.Lines[i].Time <= pos) lineIdx = i;
            else break;
        }

        if (lineIdx != _currentLineIdx)
        {
            _currentLineIdx = lineIdx;
            if (lineIdx >= 0)
            {
                var line = snap.Lyrics.Lines[lineIdx];
                if (line.IsInstrumental)
                {
                    var brk = new LyricLine(line.Time, line.EndTime, snap.Title, [new LyricWord(snap.Title, line.Time, line.EndTime)]);
                    RenderLine(brk, forceLayout: LayoutKind.Mirror);
                }
                else
                {
                    RenderLine(line);
                }
            }
        }

        UpdateWordHighlights(pos);
    }

    private void UpdateWordHighlights(double pos)
    {
        if (_activeGroup is null) return;
        ActiveWord? latestHot = null;

        for (int i = 0; i < _activeGroup.Words.Count; i++)
        {
            var aw = _activeGroup.Words[i];
            bool active = pos >= aw.Word.StartTime;
            if (active && !aw.Triggered)
            {
                _activeGroup.Words[i] = aw with { Triggered = true };
                aw.Block.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140)));
                TriggerSemanticWordMotion(aw.Block, aw.Word.Text);
            }
            if (active) latestHot = _activeGroup.Words[i];
        }

        if (latestHot?.Block != _activeGroup.HotWord)
        {
            if (_activeGroup.HotWord is not null)
                _activeGroup.HotWord.Effect = null;

            _activeGroup.HotWord = latestHot?.Block;
            if (_activeGroup.HotWord is not null)
            {
                _activeGroup.HotWord.Effect = new DropShadowEffect
                {
                    Color = _accentColor,
                    BlurRadius = 24 + _audio.Mid * 22,
                    ShadowDepth = 0,
                    Opacity = 0.85
                };
            }
        }
    }

    private void RenderLine(LyricLine line, LayoutKind? forceLayout = null)
    {
        RetireActiveGroup();

        LayoutKind layout;
        TransitionKind transition;
        IReadOnlyList<LayoutUnit> units;
        try
        {
            (layout, transition, units) = KineticLayoutEngine.Compose(
                line, ActualWidth, ActualHeight, _settings.Chaos, _lastLayout, _lastTransition);
        }
        catch (Exception ex)
        {
            App.LogCrash("RenderLine.Compose", ex);
            layout = LayoutKind.Stack;
            transition = TransitionKind.Fade;
            units = [];
        }

        if (forceLayout.HasValue)
        {
            layout = forceLayout.Value;
            (_, transition, units) = KineticLayoutEngine.Compose(
                line, ActualWidth, ActualHeight, 0, null, null);
        }

        _lastLayout = layout;
        _lastTransition = transition;

        var groupCanvas = new Canvas { Width = ActualWidth, Height = ActualHeight };
        var activeWords = new List<ActiveWord>();
        var accentBrush = new SolidColorBrush(_accentColor);
        var textBrush = new SolidColorBrush(Color.FromRgb(244, 241, 234));

        foreach (var u in units)
        {
            if (u.DecorativeText is not null)
            {
                var deco = CreateStyledTextBlock(u.DecorativeText, u, accentBrush, textBrush);
                deco.Opacity = u.Opacity;
                PlaceElement(groupCanvas, deco, u.X, u.Y, u.AngleDeg, u.IsMirror);
                continue;
            }

            var rowPanel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (int wIdx in u.WordIndices)
            {
                if (wIdx >= line.Words.Count) continue;
                var lw = line.Words[wIdx];
                var tb = CreateStyledTextBlock(lw.Text, u, accentBrush, textBrush);
                tb.Opacity = 0.22;
                tb.Margin = new Thickness(0, 0, u.FontSize * 0.24, 0);
                rowPanel.Children.Add(tb);
                activeWords.Add(new ActiveWord(tb, lw, u.IsAccent, u.IsOutline, false));
            }

            PlaceElement(groupCanvas, rowPanel, u.X, u.Y, u.AngleDeg, false, u.FromX, u.FromY, u.FromAngleDeg, u.FromScale, transition);
        }

        if (!string.IsNullOrWhiteSpace(line.AdLib))
        {
            var adBlock = new TextBlock
            {
                Text = $"({line.AdLib.ToLowerInvariant()})",
                FontFamily = new FontFamily("Consolas"),
                FontSize = Math.Max(14, ActualHeight * 0.026),
                Foreground = textBrush,
                Opacity = 0.55
            };
            Canvas.SetRight(adBlock, ActualWidth * 0.06);
            Canvas.SetTop(adBlock, ActualHeight * 0.08);
            groupCanvas.Children.Add(adBlock);
        }

        Stage.Children.Add(groupCanvas);
        _activeGroup = new ActiveLineGroup { Container = groupCanvas, Words = activeWords };
    }

    private void RetireActiveGroup()
    {
        if (_activeGroup is null) return;
        var oldCanvas = _activeGroup.Container;
        _activeGroup = null;

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(320));
        fade.Completed += (_, _) => Stage.Children.Remove(oldCanvas);
        oldCanvas.BeginAnimation(OpacityProperty, fade);
    }

    private void UpdateIdleClock()
    {
        var now = DateTime.Now;
        string timeStr = _settings.Clock24Hour ? now.ToString("HH:mm") : now.ToString("h:mm tt");
        string key = $"{timeStr}|{now.Day}";
        if (key == _clockKey) return;
        _clockKey = key;

        Stage.Children.Clear();
        var units = KineticLayoutEngine.ComposeClock(timeStr, now.ToString("dddd"), now.ToString("d MMMM"), ActualWidth, ActualHeight);
        var accentBrush = new SolidColorBrush(_accentColor);
        var textBrush = new SolidColorBrush(Color.FromRgb(244, 241, 234));
        string[] labels = [timeStr, now.ToString("dddd"), now.ToString("d MMMM")];

        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            var tb = CreateStyledTextBlock(labels[i], u, accentBrush, textBrush);
            tb.Opacity = u.Opacity;
            PlaceElement(Stage, tb, u.X, u.Y, 0, false);
        }
    }

    private static TextBlock CreateStyledTextBlock(string rawText, LayoutUnit u, Brush accentBrush, Brush textBrush)
    {
        string text = u.Uppercase ? rawText.ToUpperInvariant() : rawText;
        var tb = new TextBlock
        {
            Text = text,
            FontFamily = u.Font,
            FontWeight = u.Weight,
            FontStyle = u.Style,
            FontSize = u.FontSize,
            Foreground = u.IsOutline ? Brushes.Transparent : (u.IsAccent ? accentBrush : textBrush)
        };

        if (u.IsOutline)
        {
            tb.Effect = new DropShadowEffect
            {
                Color = u.IsAccent ? ((SolidColorBrush)accentBrush).Color : Colors.White,
                BlurRadius = 2,
                ShadowDepth = 0,
                Opacity = 0.95
            };
        }
        return tb;
    }

    private static void PlaceElement(
        Canvas parent, FrameworkElement el, double targetX, double targetY, double angleDeg, bool mirror,
        double? fromX = null, double? fromY = null, double? fromAngle = null, double? fromScale = null,
        TransitionKind transition = TransitionKind.Fade)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = el.DesiredSize.Width;
        double h = el.DesiredSize.Height;

        double left = targetX - w / 2.0;
        double top = targetY - h / 2.0;
        Canvas.SetLeft(el, left);
        Canvas.SetTop(el, top);

        var scale = new ScaleTransform(1, mirror ? -1 : 1);
        var rotate = new RotateTransform(angleDeg);
        var translate = new TranslateTransform(0, 0);

        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new TransformGroup { Children = [scale, rotate, translate] };
        parent.Children.Add(el);

        if (!fromX.HasValue || !fromY.HasValue) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(480);

        double dx = fromX.Value - targetX;
        double dy = fromY.Value - targetY;
        if (transition == TransitionKind.Drop) dy = -380;
        if (transition == TransitionKind.Rise) dy = 120;

        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, dur) { EasingFunction = ease });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, dur) { EasingFunction = ease });

        double startScale = transition switch
        {
            TransitionKind.Zoom => 2.4,
            TransitionKind.Pop => 0.25,
            _ => fromScale ?? 1.0
        };
        if (Math.Abs(startScale - 1.0) > 0.01)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(startScale, 1.0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(startScale, 1.0, dur) { EasingFunction = ease });
        }

        double startAngle = transition == TransitionKind.Spin ? angleDeg + 140 : (fromAngle ?? angleDeg);
        if (Math.Abs(startAngle - angleDeg) > 0.1)
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(startAngle, angleDeg, dur) { EasingFunction = ease });
    }

    private static void TriggerSemanticWordMotion(TextBlock tb, string word)
    {
        var w = word.Trim().ToLowerInvariant().Trim('.', ',', '!', '?');
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        tb.RenderTransformOrigin = new Point(0.5, 0.5);
        tb.RenderTransform = new TransformGroup { Children = [scale, translate] };

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(360);

        if (w is "fall" or "falling" or "drop" or "down" or "sink" or "crash")
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-24, 0, dur) { EasingFunction = new BounceEase { Bounces = 1 } });
        else if (w is "rise" or "up" or "high" or "fly" or "sky" or "lift")
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(22, 0, dur) { EasingFunction = ease });
        else if (w is "boom" or "explode" or "big" or "huge" or "loud" or "scream" or "heart" or "love")
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.32, 1.0, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.32, 1.0, dur) { EasingFunction = ease });
        }
    }

    private void UpdateAlbumArtAndAccent(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            BackgroundArt.Source = null;
            HudAlbumArt.Source = null;
            SetAccentColor(Color.FromRgb(255, 77, 109));
            return;
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();

            HudAlbumArt.Source = bmp;
            if (_settings.BlurredAlbumBg)
            {
                using var msBlur = new MemoryStream(bytes);
                var blurBmp = new BitmapImage();
                blurBmp.BeginInit();
                blurBmp.CacheOption = BitmapCacheOption.OnLoad;
                blurBmp.DecodePixelWidth = 36;
                blurBmp.StreamSource = msBlur;
                blurBmp.EndInit();
                blurBmp.Freeze();
                BackgroundArt.Source = blurBmp;
            }
            else
            {
                BackgroundArt.Source = null;
            }

            if (_settings.AutoAccent)
                SetAccentColor(ExtractVibrantAccent(bmp));
        }
        catch { }
    }

    private static Color ExtractVibrantAccent(BitmapSource src)
    {
        if (src.PixelWidth <= 0 || src.PixelHeight <= 0)
            return Color.FromRgb(255, 77, 109);

        var bgra = src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var scaled = new TransformedBitmap(bgra, new ScaleTransform(24.0 / bgra.PixelWidth, 24.0 / bgra.PixelHeight));
        int stride = scaled.PixelWidth * 4;
        var pixels = new byte[scaled.PixelHeight * stride];
        scaled.CopyPixels(pixels, stride, 0);

        double bestScore = -1;
        Color best = Color.FromRgb(255, 77, 109);

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            double b = pixels[i] / 255.0;
            double g = pixels[i + 1] / 255.0;
            double r = pixels[i + 2] / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double sat = max > 0 ? (max - min) / max : 0;
            double score = sat * max;
            if (score > bestScore && max > 0.25)
            {
                bestScore = score;
                best = Color.FromRgb(
                    (byte)Math.Clamp(r * 255 * 1.15, 60, 255),
                    (byte)Math.Clamp(g * 255 * 1.15, 60, 255),
                    (byte)Math.Clamp(b * 255 * 1.15, 60, 255));
            }
        }
        return best;
    }

    private void SetAccentColor(Color color)
    {
        _accentColor = color;
        HudProgress.Fill = new SolidColorBrush(color);
        LightStopCenter.Color = color;
        LightStopMid.Color = Color.FromArgb(95, color.R, color.G, color.B);
    }

    private void ApplySettingsToUi()
    {
        DimLayer.Opacity = Math.Clamp(_settings.BackgroundDim, 0, 0.95);
        HudCard.Visibility = _settings.ShowSongInfo ? Visibility.Visible : Visibility.Collapsed;
        double lift = Math.Max(68, ActualHeight * _settings.HudLiftPercent);
        HudCard.Margin = new Thickness(48, 0, 0, lift);
        StatusPanel.Margin = new Thickness(0, 0, 48, lift);
    }

    private void OnSyncEarlier(object sender, RoutedEventArgs e) { _songNudgeSeconds = Math.Round(_songNudgeSeconds + 0.1, 2); UpdateSyncLabel(); }
    private void OnSyncLater(object sender, RoutedEventArgs e) { _songNudgeSeconds = Math.Round(_songNudgeSeconds - 0.1, 2); UpdateSyncLabel(); }
    private void OnSyncReset(object sender, RoutedEventArgs e) { _songNudgeSeconds = 0; UpdateSyncLabel(); }
    private void UpdateSyncLabel() => SyncOffsetText.Text = $"{(_songNudgeSeconds >= 0 ? "+" : "")}{_songNudgeSeconds:0.00}s";

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Kinetic Lyrics Wallpaper",
            Visible = true
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Index Local Music Folder…", null, (_, _) =>
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "Select music folder for embedded/sidecar LRC" };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                if (_library is not null) _ = Task.Run(() => _library.Scan(dialog.SelectedPath));
        });

        var clockItem = new Forms.ToolStripMenuItem("Idle Clock") { Checked = _settings.ShowIdleClock, CheckOnClick = true };
        clockItem.CheckedChanged += (_, _) => { _settings.ShowIdleClock = clockItem.Checked; _settings.Save(); };
        menu.Items.Add(clockItem);

        var blurItem = new Forms.ToolStripMenuItem("Blurred Album Art Background") { Checked = _settings.BlurredAlbumBg, CheckOnClick = true };
        blurItem.CheckedChanged += (_, _) => { _settings.BlurredAlbumBg = blurItem.Checked; _settings.Save(); UpdateAlbumArtAndAccent(_lastThumbBytes); };
        menu.Items.Add(blurItem);

        var songInfoItem = new Forms.ToolStripMenuItem("Show Song Info HUD") { Checked = _settings.ShowSongInfo, CheckOnClick = true };
        songInfoItem.CheckedChanged += (_, _) => { _settings.ShowSongInfo = songInfoItem.Checked; _settings.Save(); ApplySettingsToUi(); };
        menu.Items.Add(songInfoItem);

        var syncMenu = new Forms.ToolStripMenuItem("Lyrics Timing Offset");
        syncMenu.DropDownItems.Add("Show Earlier (+0.10s)", null, (_, _) => Dispatcher.Invoke(() => { _songNudgeSeconds = Math.Round(_songNudgeSeconds + 0.1, 2); UpdateSyncLabel(); }));
        syncMenu.DropDownItems.Add("Show Later (-0.10s)", null, (_, _) => Dispatcher.Invoke(() => { _songNudgeSeconds = Math.Round(_songNudgeSeconds - 0.1, 2); UpdateSyncLabel(); }));
        syncMenu.DropDownItems.Add("Reset Offset (0.00s)", null, (_, _) => Dispatcher.Invoke(() => { _songNudgeSeconds = 0; UpdateSyncLabel(); }));
        menu.Items.Add(syncMenu);

        var startupItem = new Forms.ToolStripMenuItem("Start with Windows") { Checked = WallpaperSettings.IsStartupEnabled(), CheckOnClick = true };
        startupItem.CheckedChanged += (_, _) => WallpaperSettings.SetStartupEnabled(startupItem.Checked);
        menu.Items.Add(startupItem);

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
