using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace KineticWallpaper;

public partial class SettingsWindow : Window
{
    private sealed record Option(string Label, string Value);
    private sealed class Win32Owner(nint handle) : Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }

    private readonly WallpaperSettings _settings;
    private readonly Action<IReadOnlyList<string>, IReadOnlyList<string>> _onSaved;
    private readonly ObservableCollection<string> _folderDraft;
    private bool _loading = true;

    public SettingsWindow(WallpaperSettings settings, Action<IReadOnlyList<string>, IReadOnlyList<string>> onSaved)
    {
        InitializeComponent();
        _settings = settings;
        _onSaved = onSaved;
        _folderDraft = new ObservableCollection<string>(settings.MusicFolders);
        MusicFolderList.ItemsSource = _folderDraft;

        InitializeOptions();
        LoadSettings();
        ApplyPresetToControls(PresetCombo.SelectedValue?.ToString());
        _loading = false;
        UpdatePresetPanel();
        UpdateAccentState();
        UpdateSliderLabels();
    }

    private void InitializeOptions()
    {
        var monitors = new List<Option> { new("Primary monitor", "PRIMARY") };
        var screens = Forms.Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            if (!screen.Primary)
                monitors.Add(new Option($"Display {i + 1} ({screen.Bounds.Width} × {screen.Bounds.Height})", screen.DeviceName));
        }
        if (screens.Length > 1)
            monitors.Add(new Option("Span all monitors", "ALL"));
        SetOptions(MonitorCombo, monitors, NormalizeMonitor(_settings.TargetMonitor), "PRIMARY");

        SetOptions(DisplayModeCombo,
        [
            new("Behind desktop icons (wallpaper)", "auto"),
            new("Bottom-most desktop window", "bottom"),
            new("Floating window (interactive preview)", "floating")
        ], _settings.DisplayMode, "auto");

        SetOptions(ProviderCombo,
        [new("Automatic", "auto"), new("LRCLIB", "lrclib"), new("NetEase", "netease")], _settings.Provider, "auto");

        SetOptions(PresetCombo,
        [
            new("Custom", "custom"), new("Minimal", "minimal"), new("Calm", "calm"),
            new("Balanced", "balanced"), new("Hype", "hype"), new("Chaos", "chaos"),
            new("Synthwave", "synthwave"), new("Cyberpunk", "cyberpunk")
        ], _settings.Preset, "custom");

        SetOptions(LayoutCombo,
        [
            new("Auto", "auto"), new("Flow", "flow"), new("Stack", "stack"), new("Hero", "hero"),
            new("Staircase", "stair"), new("Column", "column"), new("Split", "split"), new("Grid", "grid"),
            new("Diagonal", "diag"), new("Centered", "center"), new("Zigzag", "zigzag"), new("Cross", "cross"),
            new("Frame", "frame"), new("Tower", "tower"), new("Chaos", "chaos"), new("Arc", "arc"),
            new("Pyramid", "pyramid"), new("Big initial", "initial"), new("Caps", "caps"), new("Climb", "climb"),
            new("Wave", "wave"), new("Sandwich", "sandwich"), new("Billboard", "billboard"), new("Spiral", "spiral"),
            new("Corner", "corner"), new("Echo", "echo"), new("Mirror", "mirror"), new("Quote", "quote"),
            new("Typewriter", "typewriter"), new("Headline", "headline"), new("Pillars", "pillars"),
            new("Banners", "banners"), new("Fan", "fan"), new("Ransom", "ransom"), new("Tilt", "tilt"),
            new("Bookends", "bookends"), new("Diamond", "diamond"), new("Ring", "ring"), new("Classic random", "classic")
        ], _settings.Layout, "auto");

        SetOptions(TransitionCombo,
        [
            new("Mixed", "mixed"), new("Energetic", "energetic"), new("Smooth", "smooth"), new("Letters", "letters"),
            new("Collide", "collide"), new("Gravity", "gravity"), new("Slam", "slam"), new("Drop", "drop"),
            new("Zoom", "zoom"), new("Spin", "spin"), new("Pop", "pop"), new("Wipe", "wipe"),
            new("Rise", "rise"), new("Fade", "fade"), new("Flip", "flip"), new("Swing", "swing"),
            new("Stretch", "stretch"), new("Squash", "squash"), new("Rubber", "rubber"), new("Skew", "skew"),
            new("Fly", "fly"), new("Orbit", "orbit"), new("Split", "split"), new("Iris", "iris"),
            new("Cascade", "cascade"), new("Wave", "wave")
        ], _settings.Transition, "mixed");

        SetOptions(CameraCombo,
        [
            new("Auto", "auto"), new("Follow", "follow"), new("Stream", "stream"),
            new("Edit moves", "tiktok"), new("Edit moves (subtle)", "subtle"), new("Off", "off")
        ], _settings.Camera, "auto");

        SetOptions(BackgroundCombo,
        [
            new("Album art (blurred)", "art"), new("Album colour gradient", "gradient"),
            new("Solid black", "solid"), new("Animated flowing colours", "anim"),
            new("Animated drifting album art", "drift")
        ], _settings.BgMode, "art");

        SetOptions(FpsCombo,
        [new("60 FPS — smoothest", "60"), new("30 FPS — lower GPU use", "30"), new("24 FPS — battery saver", "24")],
            _settings.TargetFps.ToString(CultureInfo.InvariantCulture), "60");
    }

    private static string NormalizeMonitor(string target)
    {
        var primary = Forms.Screen.PrimaryScreen;
        return primary is not null && string.Equals(target, primary.DeviceName, StringComparison.OrdinalIgnoreCase)
            ? "PRIMARY"
            : target;
    }

    private static void SetOptions(ComboBox combo, IEnumerable<Option> options, string selected, string fallback)
    {
        var items = options.ToList();
        combo.ItemsSource = items;
        combo.DisplayMemberPath = nameof(Option.Label);
        combo.SelectedValuePath = nameof(Option.Value);
        combo.SelectedValue = items.Any(option => option.Value == selected) ? selected : fallback;
    }

    private void LoadSettings()
    {
        MonitorCombo.SelectedValue = NormalizeMonitor(_settings.TargetMonitor);
        DisplayModeCombo.SelectedValue = _settings.DisplayMode;
        ProviderCombo.SelectedValue = _settings.Provider;
        PresetCombo.SelectedValue = _settings.Preset;
        LayoutCombo.SelectedValue = _settings.Layout;
        TransitionCombo.SelectedValue = _settings.Transition;
        CameraCombo.SelectedValue = _settings.Camera;
        BackgroundCombo.SelectedValue = _settings.BgMode;
        FpsCombo.SelectedValue = _settings.TargetFps.ToString(CultureInfo.InvariantCulture);

        ShowInfoCheck.IsChecked = _settings.ShowInfo;
        IdleClockCheck.IsChecked = _settings.IdleClock;
        Clock24Check.IsChecked = _settings.Clock24;
        StartupCheck.IsChecked = WallpaperSettings.IsStartupEnabled();
        OffsetBox.Text = _settings.Offset.ToString(CultureInfo.InvariantCulture);
        AutoSyncCheck.IsChecked = _settings.AutoSync;

        ChaosSlider.Value = _settings.Chaos;
        CameraAmountSlider.Value = _settings.CamAmount;
        DimSlider.Value = _settings.Dim;
        ReactivitySlider.Value = _settings.Reactivity;
        HudLiftSlider.Value = _settings.HudLift;
        ChoreoCheck.IsChecked = _settings.Choreo;
        CarryCheck.IsChecked = _settings.Carry;
        MoodCheck.IsChecked = _settings.MoodOn;
        FlashesCheck.IsChecked = _settings.Flashes;
        InvertCheck.IsChecked = _settings.Invert;
        AutoAccentCheck.IsChecked = _settings.AutoAccent;
        AccentBox.Text = _settings.Accent;
        LowPowerCheck.IsChecked = _settings.LowPower;
    }

    private void UpdatePresetPanel()
    {
        VisualOptionsPanel.IsEnabled = string.Equals(PresetCombo.SelectedValue?.ToString(), "custom", StringComparison.Ordinal);
    }

    private void ApplyPresetToControls(string? preset)
    {
        string transition;
        string layout;
        int chaos;
        int reactivity;
        string? background = null;
        switch (preset)
        {
            case "minimal": transition = "smooth"; layout = "flow"; chaos = 0; reactivity = 30; background = "solid"; break;
            case "calm": transition = "smooth"; layout = "auto"; chaos = 10; reactivity = 45; break;
            case "balanced": transition = "mixed"; layout = "auto"; chaos = 35; reactivity = 60; break;
            case "hype": transition = "energetic"; layout = "auto"; chaos = 60; reactivity = 100; break;
            case "chaos": transition = "mixed"; layout = "auto"; chaos = 100; reactivity = 90; break;
            case "synthwave": transition = "mixed"; layout = "auto"; chaos = 35; reactivity = 75; break;
            case "cyberpunk": transition = "energetic"; layout = "auto"; chaos = 65; reactivity = 95; break;
            default: return;
        }

        TransitionCombo.SelectedValue = transition;
        LayoutCombo.SelectedValue = layout;
        ChaosSlider.Value = chaos;
        ReactivitySlider.Value = reactivity;
        CarryCheck.IsChecked = true;
        if (background is not null)
            BackgroundCombo.SelectedValue = background;
        UpdateSliderLabels();
    }

    private void UpdateAccentState() => AccentBox.IsEnabled = AutoAccentCheck.IsChecked != true && VisualOptionsPanel.IsEnabled;

    private void UpdateSliderLabels()
    {
        ChaosValue.Text = ((int)Math.Round(ChaosSlider.Value)).ToString(CultureInfo.InvariantCulture);
        CameraAmountValue.Text = ((int)Math.Round(CameraAmountSlider.Value)).ToString(CultureInfo.InvariantCulture);
        DimValue.Text = ((int)Math.Round(DimSlider.Value)).ToString(CultureInfo.InvariantCulture);
        ReactivityValue.Text = ((int)Math.Round(ReactivitySlider.Value)).ToString(CultureInfo.InvariantCulture);
        HudLiftValue.Text = ((int)Math.Round(HudLiftSlider.Value)).ToString(CultureInfo.InvariantCulture);
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider slider || slider.Tag is not string labelName || FindName(labelName) is not TextBlock label)
            return;
        label.Text = ((int)Math.Round(slider.Value)).ToString(CultureInfo.InvariantCulture);
        if (!_loading && string.Equals(PresetCombo.SelectedValue?.ToString(), "custom", StringComparison.Ordinal))
            UpdateAccentState();
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || VisualOptionsPanel is null) return;
        ApplyPresetToControls(PresetCombo.SelectedValue?.ToString());
        UpdatePresetPanel();
        UpdateAccentState();
    }

    private void VisualCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && string.Equals(PresetCombo.SelectedValue?.ToString(), "custom", StringComparison.Ordinal))
            UpdateAccentState();
    }

    private void AutoAccentCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && AccentBox is not null)
            UpdateAccentState();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select a folder containing music files or .lrc lyrics",
            UseDescriptionForTitle = true
        };
        var owner = new Win32Owner(new WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;
        if (!_folderDraft.Any(path => string.Equals(path, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase)))
            _folderDraft.Add(dialog.SelectedPath);
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (MusicFolderList.SelectedItem is string folder)
            _folderDraft.Remove(folder);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
        {
            MessageBox.Show(this, "Enter a whole number for the lyrics timing offset.", "Invalid offset", MessageBoxButton.OK, MessageBoxImage.Information);
            OffsetBox.Focus();
            return;
        }
        if (AutoAccentCheck.IsChecked != true && !IsValidAccent(AccentBox.Text))
        {
            SettingsTabs.SelectedIndex = 2;
            MessageBox.Show(this, "Enter three colour values from 0 to 1, separated by spaces. For example: 1 0.3 0.43", "Invalid accent colour", MessageBoxButton.OK, MessageBoxImage.Information);
            AccentBox.Focus();
            return;
        }

        var addedFolders = _folderDraft
            .Where(folder => !_settings.MusicFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var removedFolders = _settings.MusicFolders
            .Where(folder => !_folderDraft.Contains(folder, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _settings.TargetMonitor = Selected(MonitorCombo, "PRIMARY");
        _settings.DisplayMode = Selected(DisplayModeCombo, "auto");
        _settings.Preset = Selected(PresetCombo, "custom");
        _settings.Provider = Selected(ProviderCombo, "auto");
        _settings.Offset = offset;
        _settings.AutoSync = AutoSyncCheck.IsChecked == true;
        _settings.ShowInfo = ShowInfoCheck.IsChecked == true;
        _settings.IdleClock = IdleClockCheck.IsChecked == true;
        _settings.Clock24 = Clock24Check.IsChecked == true;

        _settings.Layout = Selected(LayoutCombo, "auto");
        _settings.Transition = Selected(TransitionCombo, "mixed");
        _settings.Camera = Selected(CameraCombo, "auto");
        _settings.BgMode = Selected(BackgroundCombo, "art");
        _settings.Chaos = (int)Math.Round(ChaosSlider.Value);
        _settings.CamAmount = (int)Math.Round(CameraAmountSlider.Value);
        _settings.Dim = (int)Math.Round(DimSlider.Value);
        _settings.Reactivity = (int)Math.Round(ReactivitySlider.Value);
        _settings.HudLift = (int)Math.Round(HudLiftSlider.Value);
        _settings.Choreo = ChoreoCheck.IsChecked == true;
        _settings.Carry = CarryCheck.IsChecked == true;
        _settings.MoodOn = MoodCheck.IsChecked == true;
        _settings.Flashes = FlashesCheck.IsChecked == true;
        _settings.Invert = InvertCheck.IsChecked == true;
        _settings.AutoAccent = AutoAccentCheck.IsChecked == true;
        _settings.Accent = AccentBox.Text.Trim();
        _settings.TargetFps = int.TryParse(Selected(FpsCombo, "60"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fps) ? fps : 60;
        _settings.LowPower = LowPowerCheck.IsChecked == true;
        _settings.MusicFolders = _folderDraft.ToList();

        _settings.Save();
        WallpaperSettings.SetStartupEnabled(StartupCheck.IsChecked == true);
        _onSaved(addedFolders, removedFolders);
        SavedLabel.Text = "Saved. Changes are now active.";
        Close();
    }

    private static string Selected(ComboBox combo, string fallback) => combo.SelectedValue?.ToString() ?? fallback;

    private static bool IsValidAccent(string value)
    {
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && parts.All(part =>
            double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double component) &&
            double.IsFinite(component) && component is >= 0 and <= 1);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
