using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scrcap.Core;
using Scrcap.Windows.Platform.Startup;
using Scrcap.Windows.Platform.Updates;
using CoreCaptureMode = Scrcap.Core.CaptureMode;
using WpfBrush = System.Windows.Media.Brush;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfKey = System.Windows.Input.Key;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Scrcap.Windows.UI.Preferences;

public sealed class PreferencesViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore store;
    private readonly ILaunchAtLoginService launchAtLoginService;
    private readonly IUpdateChecker updateChecker;
    private AppAction? recordingAction;
    private ThemeMode selectedThemeMode;
    private bool launchAtLogin;
    private bool includeCursor;
    private bool includeWindowShadow;
    private bool windowBackgroundTransparent;
    private string windowBackgroundHex = "#FFFFFF";
    private int captureDelaySeconds;
    private int scrollingMaxHeight;
    private WindowCaptureTarget windowCaptureTarget;
    private AfterCaptureBehavior regionAfterCapture;
    private AfterCaptureBehavior windowAfterCapture;
    private AfterCaptureBehavior fullscreenAfterCapture;
    private AfterCaptureBehavior scrollingAfterCapture;
    private string captureRegionHotkey = string.Empty;
    private string captureWindowHotkey = string.Empty;
    private string captureFullscreenHotkey = string.Empty;
    private string captureScrollingHotkey = string.Empty;
    private string captureDelayedHotkey = string.Empty;
    private string repeatLastHotkey = string.Empty;
    private string shortcutRecorderStatus = "Press a shortcut while focused in a shortcut field.";
    private string palette1 = string.Empty;
    private string palette2 = string.Empty;
    private string palette3 = string.Empty;
    private string palette4 = string.Empty;
    private string palette5 = string.Empty;
    private double strokeWidth;
    private double textSize;
    private TextEnterBehavior textEnterBehavior;
    private EscBehavior escBehavior;
    private bool autoExpandCanvas;
    private string canvasExtensionBackgroundHex = "#FFFFFF";
    private string saveFolder = string.Empty;
    private string filenamePattern = string.Empty;
    private int exportScale;
    private bool suppressCopyNotification;
    private string? lastError;
    private string? updateStatus;
    private Uri? latestReleaseUrl;
    private bool isCheckingForUpdates;

    public PreferencesViewModel(
        SettingsStore store,
        ILaunchAtLoginService? launchAtLoginService = null,
        IUpdateChecker? updateChecker = null)
    {
        this.store = store;
        this.launchAtLoginService = launchAtLoginService ?? new StartupFolderLaunchAtLoginService();
        this.updateChecker = updateChecker ?? new GitHubUpdateChecker();
        Load(store.Settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? SettingsChanged
    {
        add => store.SettingsChanged += value;
        remove => store.SettingsChanged -= value;
    }

    public IReadOnlyList<ThemeMode> ThemeModes { get; } = [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public IReadOnlyList<AfterCaptureBehavior> CaptureBehaviors { get; } = [AfterCaptureBehavior.OpenEditor, AfterCaptureBehavior.CopyOnly, AfterCaptureBehavior.Both];

    public IReadOnlyList<WindowCaptureTarget> WindowTargets { get; } = [WindowCaptureTarget.Active, WindowCaptureTarget.Selected];

    public IReadOnlyList<TextEnterBehavior> TextEnterBehaviors { get; } = [TextEnterBehavior.Newline, TextEnterBehavior.Commit];

    public IReadOnlyList<EscBehavior> EscBehaviors { get; } = [EscBehavior.CopyAndClose, EscBehavior.CloseOnly];

    public IReadOnlyList<int> ExportScales { get; } = [1, 2];

    public ThemeMode SelectedThemeMode
    {
        get => selectedThemeMode;
        set => SetAndPersist(ref selectedThemeMode, value, settings => settings.ThemeMode = value);
    }

    public bool LaunchAtLogin
    {
        get => launchAtLogin;
        set
        {
            if (value == launchAtLogin)
            {
                return;
            }

            var registration = launchAtLoginService.SetEnabled(value);
            if (!registration.Succeeded)
            {
                SetError("Could not update Launch at Login. " + (registration.ErrorMessage ?? "Windows rejected the Startup registration."));
                OnPropertyChanged();
                return;
            }

            var previous = launchAtLogin;
            if (SetAndPersist(ref launchAtLogin, value, settings => settings.LaunchAtLogin = value))
            {
                return;
            }

            _ = launchAtLoginService.SetEnabled(previous);
        }
    }

    public bool IncludeCursor
    {
        get => includeCursor;
        set => SetAndPersist(ref includeCursor, value, settings => settings.IncludeCursor = value);
    }

    public bool IncludeWindowShadow
    {
        get => includeWindowShadow;
        set => SetAndPersist(ref includeWindowShadow, value, settings => settings.IncludeWindowShadow = value);
    }

    public bool WindowBackgroundTransparent
    {
        get => windowBackgroundTransparent;
        set => SetAndPersist(ref windowBackgroundTransparent, value, settings => settings.WindowBackgroundTransparent = value);
    }

    public string WindowBackgroundHex
    {
        get => windowBackgroundHex;
        set => SetColorAndPersist(ref windowBackgroundHex, value, (settings, normalized) => settings.WindowBackgroundHex = normalized, nameof(WindowBackgroundBrush));
    }

    public int CaptureDelaySeconds
    {
        get => captureDelaySeconds;
        set => SetAndPersist(ref captureDelaySeconds, value, settings => settings.CaptureDelaySeconds = value);
    }

    public int ScrollingMaxHeight
    {
        get => scrollingMaxHeight;
        set => SetAndPersist(ref scrollingMaxHeight, value, settings => settings.ScrollingMaxHeight = value);
    }

    public WindowCaptureTarget WindowCaptureTarget
    {
        get => windowCaptureTarget;
        set => SetAndPersist(ref windowCaptureTarget, value, settings => settings.WindowCaptureTarget = value);
    }

    public AfterCaptureBehavior RegionAfterCapture
    {
        get => regionAfterCapture;
        set => SetAndPersist(ref regionAfterCapture, value, settings => settings.AfterCapture[CoreCaptureMode.Region.StorageKey()] = value);
    }

    public AfterCaptureBehavior WindowAfterCapture
    {
        get => windowAfterCapture;
        set => SetAndPersist(ref windowAfterCapture, value, settings => settings.AfterCapture[CoreCaptureMode.Window.StorageKey()] = value);
    }

    public AfterCaptureBehavior FullscreenAfterCapture
    {
        get => fullscreenAfterCapture;
        set => SetAndPersist(ref fullscreenAfterCapture, value, settings => settings.AfterCapture[CoreCaptureMode.Fullscreen.StorageKey()] = value);
    }

    public AfterCaptureBehavior ScrollingAfterCapture
    {
        get => scrollingAfterCapture;
        set => SetAndPersist(ref scrollingAfterCapture, value, settings => settings.AfterCapture[CoreCaptureMode.Scrolling.StorageKey()] = value);
    }

    public string CaptureRegionHotkey { get => captureRegionHotkey; private set => SetShortcutField(ref captureRegionHotkey, value, nameof(CaptureRegionHotkey), nameof(CaptureRegionHotkeyDisplay)); }

    public string CaptureRegionHotkeyDisplay => HotkeyDisplay(AppAction.CaptureRegion);

    public string CaptureWindowHotkey { get => captureWindowHotkey; private set => SetShortcutField(ref captureWindowHotkey, value, nameof(CaptureWindowHotkey), nameof(CaptureWindowHotkeyDisplay)); }

    public string CaptureWindowHotkeyDisplay => HotkeyDisplay(AppAction.CaptureWindow);

    public string CaptureFullscreenHotkey { get => captureFullscreenHotkey; private set => SetShortcutField(ref captureFullscreenHotkey, value, nameof(CaptureFullscreenHotkey), nameof(CaptureFullscreenHotkeyDisplay)); }

    public string CaptureFullscreenHotkeyDisplay => HotkeyDisplay(AppAction.CaptureFullscreen);

    public string CaptureScrollingHotkey { get => captureScrollingHotkey; private set => SetShortcutField(ref captureScrollingHotkey, value, nameof(CaptureScrollingHotkey), nameof(CaptureScrollingHotkeyDisplay)); }

    public string CaptureScrollingHotkeyDisplay => HotkeyDisplay(AppAction.CaptureScrolling);

    public string CaptureDelayedHotkey { get => captureDelayedHotkey; private set => SetShortcutField(ref captureDelayedHotkey, value, nameof(CaptureDelayedHotkey), nameof(CaptureDelayedHotkeyDisplay)); }

    public string CaptureDelayedHotkeyDisplay => HotkeyDisplay(AppAction.CaptureDelayed);

    public string RepeatLastHotkey { get => repeatLastHotkey; private set => SetShortcutField(ref repeatLastHotkey, value, nameof(RepeatLastHotkey), nameof(RepeatLastHotkeyDisplay)); }

    public string RepeatLastHotkeyDisplay => HotkeyDisplay(AppAction.RepeatLast);

    public string ShortcutRecorderStatus { get => shortcutRecorderStatus; private set => SetLocal(ref shortcutRecorderStatus, value); }

    public string Palette1 { get => palette1; set => SetPalette(0, ref palette1, value, nameof(Palette1), nameof(Palette1Brush)); }

    public string Palette2 { get => palette2; set => SetPalette(1, ref palette2, value, nameof(Palette2), nameof(Palette2Brush)); }

    public string Palette3 { get => palette3; set => SetPalette(2, ref palette3, value, nameof(Palette3), nameof(Palette3Brush)); }

    public string Palette4 { get => palette4; set => SetPalette(3, ref palette4, value, nameof(Palette4), nameof(Palette4Brush)); }

    public string Palette5 { get => palette5; set => SetPalette(4, ref palette5, value, nameof(Palette5), nameof(Palette5Brush)); }

    public WpfBrush Palette1Brush => BrushFromHex(Palette1);

    public WpfBrush Palette2Brush => BrushFromHex(Palette2);

    public WpfBrush Palette3Brush => BrushFromHex(Palette3);

    public WpfBrush Palette4Brush => BrushFromHex(Palette4);

    public WpfBrush Palette5Brush => BrushFromHex(Palette5);

    public WpfBrush WindowBackgroundBrush => BrushFromHex(WindowBackgroundHex);

    public double StrokeWidth
    {
        get => strokeWidth;
        set => SetAndPersist(ref strokeWidth, value, settings => settings.StrokeWidth = value);
    }

    public double TextSize
    {
        get => textSize;
        set => SetAndPersist(ref textSize, value, settings => settings.TextSize = value);
    }

    public TextEnterBehavior TextEnterBehavior
    {
        get => textEnterBehavior;
        set => SetAndPersist(ref textEnterBehavior, value, settings => settings.TextEnterBehavior = value);
    }

    public EscBehavior EscBehavior
    {
        get => escBehavior;
        set => SetAndPersist(ref escBehavior, value, settings => settings.EscBehavior = value);
    }

    public bool AutoExpandCanvas
    {
        get => autoExpandCanvas;
        set => SetAndPersist(ref autoExpandCanvas, value, settings => settings.AutoExpandCanvas = value);
    }

    public string CanvasExtensionBackgroundHex
    {
        get => canvasExtensionBackgroundHex;
        set => SetColorAndPersist(ref canvasExtensionBackgroundHex, value, (settings, normalized) => settings.CanvasExtensionBackgroundHex = normalized, nameof(CanvasExtensionBackgroundBrush));
    }

    public WpfBrush CanvasExtensionBackgroundBrush => BrushFromHex(CanvasExtensionBackgroundHex);

    public string SaveFolder
    {
        get => saveFolder;
        set => SetAndPersist(ref saveFolder, value, settings => settings.SaveFolder = string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public string FilenamePattern
    {
        get => filenamePattern;
        set => SetAndPersist(ref filenamePattern, value, settings => settings.FilenamePattern = value);
    }

    public int ExportScale
    {
        get => exportScale;
        set
        {
            var normalized = value == 1 ? 1 : 2;
            SetAndPersist(ref exportScale, normalized, settings => settings.ExportScale = normalized);
        }
    }

    public bool SuppressCopyNotification
    {
        get => suppressCopyNotification;
        set => SetAndPersist(ref suppressCopyNotification, value, settings => settings.SuppressCopyNotification = value);
    }

    public string? LastError { get => lastError; private set => SetLocal(ref lastError, value); }

    public string? UpdateStatus { get => updateStatus; private set => SetLocal(ref updateStatus, value); }

    public Uri? LatestReleaseUrl { get => latestReleaseUrl; private set => SetLocal(ref latestReleaseUrl, value); }

    public bool HasLatestRelease => LatestReleaseUrl is not null;

    public bool IsCheckingForUpdates { get => isCheckingForUpdates; private set => SetLocal(ref isCheckingForUpdates, value); }

    public string VersionText => typeof(PreferencesViewModel).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public string BuildChannel => "local";

    public string SourceLink => GitHubUpdateChecker.SourceUrl;

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsCheckingForUpdates)
        {
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = "Checking GitHub for updates...";
        LatestReleaseUrl = null;
        OnPropertyChanged(nameof(HasLatestRelease));
        try
        {
            var result = await updateChecker.CheckAsync(VersionText, cancellationToken);
            LatestReleaseUrl = result.Release.HtmlUrl;
            OnPropertyChanged(nameof(HasLatestRelease));
            UpdateStatus = result.Availability switch
            {
                UpdateAvailability.Available => $"scrcap {result.Release.TagName} is available. You are running {result.CurrentVersion}.",
                UpdateAvailability.UpToDate => $"You are up to date ({result.CurrentVersion}). Latest release is {result.Release.TagName}.",
                _ => $"Latest release is {result.Release.TagName}; this {result.CurrentVersion} build cannot be compared automatically.",
            };
            LastError = null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            SetError("Could not check for updates. The GitHub request timed out.");
            UpdateStatus = null;
        }
        catch (Exception ex)
        {
            SetError("Could not check for updates. " + ex.Message);
            UpdateStatus = null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    public void BeginShortcutRecording(AppAction action)
    {
        if (recordingAction == action)
        {
            return;
        }

        var previous = recordingAction;
        recordingAction = action;
        if (previous is { } previousAction)
        {
            OnHotkeyDisplayChanged(previousAction);
        }

        ShortcutRecorderStatus = $"Recording {action.Title()} shortcut. Press a key combination now; Backspace clears.";
        OnHotkeyDisplayChanged(action);
    }

    public void EndShortcutRecording(AppAction action)
    {
        if (recordingAction != action)
        {
            return;
        }

        recordingAction = null;
        OnHotkeyDisplayChanged(action);
    }

    public bool RecordShortcut(AppAction action, WpfKey key, WpfModifierKeys modifiers)
    {
        if (IsClearKey(key))
        {
            return PersistHotkeys(action, string.Empty, null, $"{action.Title()} shortcut cleared.");
        }

        if (!TryBuildChord(key, modifiers, out var chord))
        {
            ShortcutRecorderStatus = "Use at least one modifier plus a letter, number, function, or navigation key.";
            return false;
        }

        if (!Keymap.IsUsableGlobalShortcut(chord))
        {
            ShortcutRecorderStatus = $"{chord.WindowsDisplayValue} is reserved by Windows.";
            return false;
        }

        var conflictedAction = CurrentHotkeys()
            .Where(pair => pair.Key != action && pair.Value == chord)
            .Select(pair => (AppAction?)pair.Key)
            .FirstOrDefault();
        var status = conflictedAction is null
            ? $"{action.Title()} set to {chord.WindowsDisplayValue}."
            : $"{action.Title()} set to {chord.WindowsDisplayValue}; {conflictedAction.Value.Title()} was cleared.";
        return PersistHotkeys(action, chord.StringValue, conflictedAction, status);
    }

    public void ResetAll()
    {
        var previousLaunchAtLogin = launchAtLogin;
        var launchResult = previousLaunchAtLogin
            ? launchAtLoginService.SetEnabled(false)
            : LaunchAtLoginResult.Success;
        if (!launchResult.Succeeded)
        {
            SetError("Could not reset Launch at Login. " + (launchResult.ErrorMessage ?? "Windows rejected the Startup change."));
            return;
        }

        var defaults = Settings.Defaults();
        if (!store.Update(settings => CopySettings(defaults, settings)))
        {
            _ = launchAtLoginService.SetEnabled(previousLaunchAtLogin);
            SetError("Could not reset preferences. The previous valid settings file was kept.");
            return;
        }

        Load(store.Settings);
        LastError = null;
        OnPropertyChanged(string.Empty);
    }

    public bool SetColor(string propertyName, string hex)
    {
        var normalized = Settings.NormalizeHexColor(hex);
        if (normalized is null)
        {
            return false;
        }

        switch (propertyName)
        {
            case nameof(WindowBackgroundHex): WindowBackgroundHex = normalized; break;
            case nameof(Palette1): Palette1 = normalized; break;
            case nameof(Palette2): Palette2 = normalized; break;
            case nameof(Palette3): Palette3 = normalized; break;
            case nameof(Palette4): Palette4 = normalized; break;
            case nameof(Palette5): Palette5 = normalized; break;
            case nameof(CanvasExtensionBackgroundHex): CanvasExtensionBackgroundHex = normalized; break;
            default: return false;
        }

        return true;
    }

    // Retained for source compatibility with older automation. Changes are already persisted by each setter.
    public bool Save() => LastError is null;

    private bool SetAndPersist<T>(ref T field, T value, Action<Settings> mutate, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        if (!store.Update(mutate))
        {
            SetError("Could not save preferences. The previous valid settings file was kept.");
            OnPropertyChanged(propertyName);
            return false;
        }

        field = value;
        LastError = null;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetColorAndPersist(ref string field, string value, Action<Settings, string> mutate, string brushProperty, [CallerMemberName] string? propertyName = null)
    {
        var normalized = Settings.NormalizeHexColor(value);
        if (normalized is null || !SetAndPersist(ref field, normalized, settings => mutate(settings, normalized), propertyName))
        {
            return;
        }

        OnPropertyChanged(brushProperty);
    }

    private void SetPalette(int index, ref string field, string value, string propertyName, string brushProperty)
    {
        var normalized = Settings.NormalizeHexColor(value);
        if (normalized is null)
        {
            return;
        }

        if (SetAndPersist(ref field, normalized, settings => settings.PaletteHex = CurrentPaletteWith(index, normalized), propertyName))
        {
            OnPropertyChanged(brushProperty);
        }
    }

    private bool PersistHotkeys(AppAction action, string value, AppAction? conflictedAction, string status)
    {
        if (!store.Update(settings =>
            {
                settings.Hotkeys[action.StorageKey()] = value;
                if (conflictedAction is { } victim)
                {
                    settings.Hotkeys[victim.StorageKey()] = string.Empty;
                }
            }))
        {
            SetError("Could not save the shortcut. The previous valid settings file was kept.");
            return false;
        }

        if (conflictedAction is { } victimAction)
        {
            SetHotkeyLocal(victimAction, string.Empty);
        }

        recordingAction = null;
        SetHotkeyLocal(action, value);
        ShortcutRecorderStatus = status;
        LastError = null;
        return true;
    }

    private void Load(Settings settings)
    {
        selectedThemeMode = settings.ThemeMode;
        launchAtLogin = settings.LaunchAtLogin;
        includeCursor = settings.IncludeCursor;
        includeWindowShadow = settings.IncludeWindowShadow;
        windowBackgroundTransparent = settings.WindowBackgroundTransparent;
        windowBackgroundHex = settings.WindowBackgroundHex;
        captureDelaySeconds = settings.CaptureDelaySeconds;
        scrollingMaxHeight = settings.ScrollingMaxHeight;
        windowCaptureTarget = settings.WindowCaptureTarget;
        regionAfterCapture = settings.BehaviorFor(CoreCaptureMode.Region);
        windowAfterCapture = settings.BehaviorFor(CoreCaptureMode.Window);
        fullscreenAfterCapture = settings.BehaviorFor(CoreCaptureMode.Fullscreen);
        scrollingAfterCapture = settings.BehaviorFor(CoreCaptureMode.Scrolling);
        captureRegionHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureRegion.StorageKey(), string.Empty);
        captureWindowHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureWindow.StorageKey(), string.Empty);
        captureFullscreenHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureFullscreen.StorageKey(), string.Empty);
        captureScrollingHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureScrolling.StorageKey(), string.Empty);
        captureDelayedHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureDelayed.StorageKey(), string.Empty);
        repeatLastHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.RepeatLast.StorageKey(), string.Empty);
        palette1 = settings.PaletteHex.ElementAtOrDefault(0) ?? Settings.DefaultPalette[0];
        palette2 = settings.PaletteHex.ElementAtOrDefault(1) ?? Settings.DefaultPalette[1];
        palette3 = settings.PaletteHex.ElementAtOrDefault(2) ?? Settings.DefaultPalette[2];
        palette4 = settings.PaletteHex.ElementAtOrDefault(3) ?? Settings.DefaultPalette[3];
        palette5 = settings.PaletteHex.ElementAtOrDefault(4) ?? Settings.DefaultPalette[4];
        strokeWidth = settings.StrokeWidth;
        textSize = settings.TextSize;
        textEnterBehavior = settings.TextEnterBehavior;
        escBehavior = settings.EscBehavior;
        autoExpandCanvas = settings.AutoExpandCanvas;
        canvasExtensionBackgroundHex = settings.CanvasExtensionBackgroundHex;
        saveFolder = settings.SaveFolder ?? string.Empty;
        filenamePattern = settings.FilenamePattern;
        exportScale = settings.ExportScale;
        suppressCopyNotification = settings.SuppressCopyNotification;
    }

    private List<string> CurrentPaletteWith(int index, string value)
    {
        var palette = new List<string> { Palette1, Palette2, Palette3, Palette4, Palette5 };
        palette[index] = value;
        return palette;
    }

    private static WpfBrush BrushFromHex(string hex)
    {
        var normalized = Settings.NormalizeHexColor(hex) ?? "#FFFFFF";
        var brush = new WpfSolidColorBrush((System.Windows.Media.Color)WpfColorConverter.ConvertFromString(normalized));
        brush.Freeze();
        return brush;
    }

    private Dictionary<AppAction, KeyChord> CurrentHotkeys()
    {
        var result = new Dictionary<AppAction, KeyChord>();
        foreach (var action in AppActionExtensions.ShortcutOrder)
        {
            if (KeyChord.TryParse(GetHotkey(action), out var chord))
            {
                result[action] = chord;
            }
        }

        return result;
    }

    private string GetHotkey(AppAction action) => action switch
    {
        AppAction.CaptureRegion => CaptureRegionHotkey,
        AppAction.CaptureWindow => CaptureWindowHotkey,
        AppAction.CaptureFullscreen => CaptureFullscreenHotkey,
        AppAction.CaptureScrolling => CaptureScrollingHotkey,
        AppAction.CaptureDelayed => CaptureDelayedHotkey,
        AppAction.RepeatLast => RepeatLastHotkey,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private string HotkeyDisplay(AppAction action)
    {
        if (recordingAction == action)
        {
            return "Recording...";
        }

        var value = GetHotkey(action);
        return KeyChord.TryParse(value, out var chord) ? chord.WindowsDisplayValue : value;
    }

    private void SetHotkeyLocal(AppAction action, string value)
    {
        switch (action)
        {
            case AppAction.CaptureRegion: CaptureRegionHotkey = value; break;
            case AppAction.CaptureWindow: CaptureWindowHotkey = value; break;
            case AppAction.CaptureFullscreen: CaptureFullscreenHotkey = value; break;
            case AppAction.CaptureScrolling: CaptureScrollingHotkey = value; break;
            case AppAction.CaptureDelayed: CaptureDelayedHotkey = value; break;
            case AppAction.RepeatLast: RepeatLastHotkey = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private void SetShortcutField(ref string field, string value, string propertyName, string displayPropertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(displayPropertyName);
    }

    private void OnHotkeyDisplayChanged(AppAction action) => OnPropertyChanged(action switch
    {
        AppAction.CaptureRegion => nameof(CaptureRegionHotkeyDisplay),
        AppAction.CaptureWindow => nameof(CaptureWindowHotkeyDisplay),
        AppAction.CaptureFullscreen => nameof(CaptureFullscreenHotkeyDisplay),
        AppAction.CaptureScrolling => nameof(CaptureScrollingHotkeyDisplay),
        AppAction.CaptureDelayed => nameof(CaptureDelayedHotkeyDisplay),
        AppAction.RepeatLast => nameof(RepeatLastHotkeyDisplay),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    });

    private static bool TryBuildChord(WpfKey key, WpfModifierKeys modifiers, out KeyChord chord)
    {
        chord = default;
        if (IsModifierKey(key) || !TryKeyName(key, out var keyName))
        {
            return false;
        }

        var chordModifiers = ChordModifiers.None;
        if (modifiers.HasFlag(WpfModifierKeys.Control)) chordModifiers |= ChordModifiers.Control;
        if (modifiers.HasFlag(WpfModifierKeys.Alt)) chordModifiers |= ChordModifiers.Option;
        if (modifiers.HasFlag(WpfModifierKeys.Shift)) chordModifiers |= ChordModifiers.Shift;
        if (modifiers.HasFlag(WpfModifierKeys.Windows)) chordModifiers |= ChordModifiers.Command;
        chord = new KeyChord(keyName, chordModifiers);
        return true;
    }

    private static bool TryKeyName(WpfKey key, out string name)
    {
        name = key switch
        {
            >= WpfKey.A and <= WpfKey.Z => key.ToString().ToLowerInvariant(),
            >= WpfKey.D0 and <= WpfKey.D9 => ((int)(key - WpfKey.D0)).ToString(),
            >= WpfKey.NumPad0 and <= WpfKey.NumPad9 => ((int)(key - WpfKey.NumPad0)).ToString(),
            >= WpfKey.F1 and <= WpfKey.F24 => key.ToString().ToLowerInvariant(),
            WpfKey.Tab => "tab",
            WpfKey.Delete => "delete",
            WpfKey.Back => "backspace",
            WpfKey.Space => "space",
            WpfKey.Enter or WpfKey.Return => "enter",
            WpfKey.Insert => "insert",
            WpfKey.Home => "home",
            WpfKey.End => "end",
            WpfKey.PageUp => "pageup",
            WpfKey.PageDown => "pagedown",
            WpfKey.Up => "up",
            WpfKey.Down => "down",
            WpfKey.Left => "left",
            WpfKey.Right => "right",
            _ => string.Empty,
        };
        return name.Length > 0;
    }

    private static bool IsClearKey(WpfKey key) => key is WpfKey.Back or WpfKey.Delete;

    private static bool IsModifierKey(WpfKey key) =>
        key is WpfKey.LeftCtrl or WpfKey.RightCtrl or WpfKey.LeftAlt or WpfKey.RightAlt or WpfKey.LeftShift or WpfKey.RightShift or WpfKey.LWin or WpfKey.RWin;

    private static void CopySettings(Settings source, Settings target)
    {
        target.SchemaVersion = source.SchemaVersion;
        target.Hotkeys = new Dictionary<string, string>(source.Hotkeys);
        target.AfterCapture = new Dictionary<string, AfterCaptureBehavior>(source.AfterCapture);
        target.PaletteHex = source.PaletteHex.ToList();
        target.EscBehavior = source.EscBehavior;
        target.StrokeWidth = source.StrokeWidth;
        target.TextSize = source.TextSize;
        target.TextEnterBehavior = source.TextEnterBehavior;
        target.WindowCaptureTarget = source.WindowCaptureTarget;
        target.IncludeWindowShadow = source.IncludeWindowShadow;
        target.WindowBackgroundTransparent = source.WindowBackgroundTransparent;
        target.WindowBackgroundHex = source.WindowBackgroundHex;
        target.AutoExpandCanvas = source.AutoExpandCanvas;
        target.CanvasExtensionBackgroundHex = source.CanvasExtensionBackgroundHex;
        target.SaveFolder = source.SaveFolder;
        target.FilenamePattern = source.FilenamePattern;
        target.ExportScale = source.ExportScale;
        target.ScrollingMaxHeight = source.ScrollingMaxHeight;
        target.LaunchAtLogin = source.LaunchAtLogin;
        target.ThemeMode = source.ThemeMode;
        target.IncludeCursor = source.IncludeCursor;
        target.CaptureDelaySeconds = source.CaptureDelaySeconds;
        target.SuppressCopyNotification = source.SuppressCopyNotification;
        target.HasShownFirstLaunchNotice = source.HasShownFirstLaunchNotice;
    }

    private void SetError(string message) => LastError = message;

    private void SetLocal<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
