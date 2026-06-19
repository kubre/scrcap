using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using Scrcap.Core;
using Scrcap.Windows.Platform.Startup;
using CoreCaptureMode = Scrcap.Core.CaptureMode;
using WpfKey = System.Windows.Input.Key;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace Scrcap.Windows.UI.Preferences;

public sealed class PreferencesViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore store;
    private readonly ILaunchAtLoginService launchAtLoginService;
    private readonly DispatcherTimer debounceTimer;
    private readonly Dictionary<string, Action<Settings>> pendingWrites = [];
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
    private string shortcutRecorderStatus = "Click a shortcut, then press a Windows shortcut. Esc cancels; Delete clears.";
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
    private bool notifyWhenCopied;
    private string? lastError;

    public PreferencesViewModel(SettingsStore store, ILaunchAtLoginService? launchAtLoginService = null)
    {
        this.store = store;
        this.launchAtLoginService = launchAtLoginService ?? new StartupFolderLaunchAtLoginService();
        debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        debounceTimer.Tick += (_, _) => FlushPendingWrites();
        Load(store.Settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<ThemeMode>? ThemeModeChanged;

    public IReadOnlyList<PreferenceOption<ThemeMode>> ThemeModeOptions { get; } =
    [
        new(ThemeMode.System, "System"),
        new(ThemeMode.Light, "Light"),
        new(ThemeMode.Dark, "Dark"),
    ];

    public IReadOnlyList<PreferenceOption<AfterCaptureBehavior>> CaptureBehaviorOptions { get; } =
    [
        new(AfterCaptureBehavior.OpenEditor, "Open editor"),
        new(AfterCaptureBehavior.CopyOnly, "Copy only"),
        new(AfterCaptureBehavior.Both, "Copy + editor"),
    ];

    public IReadOnlyList<PreferenceOption<WindowCaptureTarget>> WindowTargetOptions { get; } =
    [
        new(WindowCaptureTarget.Active, "Frontmost"),
        new(WindowCaptureTarget.Selected, "Pick on screen"),
    ];

    public IReadOnlyList<PreferenceOption<TextEnterBehavior>> TextEnterBehaviorOptions { get; } =
    [
        new(TextEnterBehavior.Newline, "New line"),
        new(TextEnterBehavior.Commit, "Commit text"),
    ];

    public IReadOnlyList<PreferenceOption<EscBehavior>> EscBehaviorOptions { get; } =
    [
        new(EscBehavior.CopyAndClose, "Copy + close"),
        new(EscBehavior.CloseOnly, "Close only"),
    ];

    public string SettingsPathCaption => store.FilePath;

    public ThemeMode SelectedThemeMode
    {
        get => selectedThemeMode;
        set
        {
            if (SetAndPersist(ref selectedThemeMode, value, settings => settings.ThemeMode = value))
            {
                OnPropertyChanged(nameof(IsThemeSystem));
                OnPropertyChanged(nameof(IsThemeLight));
                OnPropertyChanged(nameof(IsThemeDark));
                ThemeModeChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsThemeSystem
    {
        get => SelectedThemeMode == ThemeMode.System;
        set { if (value) SelectedThemeMode = ThemeMode.System; }
    }

    public bool IsThemeLight
    {
        get => SelectedThemeMode == ThemeMode.Light;
        set { if (value) SelectedThemeMode = ThemeMode.Light; }
    }

    public bool IsThemeDark
    {
        get => SelectedThemeMode == ThemeMode.Dark;
        set { if (value) SelectedThemeMode = ThemeMode.Dark; }
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

            if (!launchAtLoginService.SetEnabled(value))
            {
                SetError("Could not update Launch at Login. Your saved settings were left unchanged.");
                return;
            }

            if (!SetAndPersist(ref launchAtLogin, value, settings => settings.LaunchAtLogin = value))
            {
                _ = launchAtLoginService.SetEnabled(store.Settings.LaunchAtLogin);
            }
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
        set
        {
            if (SetAndPersist(ref windowBackgroundTransparent, value, settings => settings.WindowBackgroundTransparent = value))
            {
                OnPropertyChanged(nameof(IsWindowBackgroundColorEnabled));
            }
        }
    }

    public bool IsWindowBackgroundColorEnabled => !WindowBackgroundTransparent;

    public string WindowBackgroundHex
    {
        get => windowBackgroundHex;
        set => SetDebounced(ref windowBackgroundHex, NormalizeHex(value, windowBackgroundHex), settings => settings.WindowBackgroundHex = WindowBackgroundHex);
    }

    public int CaptureDelaySeconds
    {
        get => captureDelaySeconds;
        set => SetDebounced(ref captureDelaySeconds, value, settings => settings.CaptureDelaySeconds = CaptureDelaySeconds);
    }

    public int ScrollingMaxHeight
    {
        get => scrollingMaxHeight;
        set => SetDebounced(ref scrollingMaxHeight, value, settings => settings.ScrollingMaxHeight = ScrollingMaxHeight);
    }

    public WindowCaptureTarget WindowCaptureTarget
    {
        get => windowCaptureTarget;
        set => SetAndPersist(ref windowCaptureTarget, value, settings => settings.WindowCaptureTarget = value);
    }

    public AfterCaptureBehavior RegionAfterCapture
    {
        get => regionAfterCapture;
        set => SetAndPersist(ref regionAfterCapture, value, settings => SetAfterCapture(settings, CoreCaptureMode.Region, value));
    }

    public AfterCaptureBehavior WindowAfterCapture
    {
        get => windowAfterCapture;
        set => SetAndPersist(ref windowAfterCapture, value, settings => SetAfterCapture(settings, CoreCaptureMode.Window, value));
    }

    public AfterCaptureBehavior FullscreenAfterCapture
    {
        get => fullscreenAfterCapture;
        set => SetAndPersist(ref fullscreenAfterCapture, value, settings => SetAfterCapture(settings, CoreCaptureMode.Fullscreen, value));
    }

    public AfterCaptureBehavior ScrollingAfterCapture
    {
        get => scrollingAfterCapture;
        set => SetAndPersist(ref scrollingAfterCapture, value, settings => SetAfterCapture(settings, CoreCaptureMode.Scrolling, value));
    }

    public string CaptureRegionHotkey
    {
        get => captureRegionHotkey;
        private set => SetShortcutField(ref captureRegionHotkey, value, nameof(CaptureRegionHotkey), nameof(CaptureRegionHotkeyDisplay));
    }

    public string CaptureRegionHotkeyDisplay => DisplayShortcut(CaptureRegionHotkey);

    public string CaptureWindowHotkey
    {
        get => captureWindowHotkey;
        private set => SetShortcutField(ref captureWindowHotkey, value, nameof(CaptureWindowHotkey), nameof(CaptureWindowHotkeyDisplay));
    }

    public string CaptureWindowHotkeyDisplay => DisplayShortcut(CaptureWindowHotkey);

    public string CaptureFullscreenHotkey
    {
        get => captureFullscreenHotkey;
        private set => SetShortcutField(ref captureFullscreenHotkey, value, nameof(CaptureFullscreenHotkey), nameof(CaptureFullscreenHotkeyDisplay));
    }

    public string CaptureFullscreenHotkeyDisplay => DisplayShortcut(CaptureFullscreenHotkey);

    public string CaptureScrollingHotkey
    {
        get => captureScrollingHotkey;
        private set => SetShortcutField(ref captureScrollingHotkey, value, nameof(CaptureScrollingHotkey), nameof(CaptureScrollingHotkeyDisplay));
    }

    public string CaptureScrollingHotkeyDisplay => DisplayShortcut(CaptureScrollingHotkey);

    public string CaptureDelayedHotkey
    {
        get => captureDelayedHotkey;
        private set => SetShortcutField(ref captureDelayedHotkey, value, nameof(CaptureDelayedHotkey), nameof(CaptureDelayedHotkeyDisplay));
    }

    public string CaptureDelayedHotkeyDisplay => DisplayShortcut(CaptureDelayedHotkey);

    public string RepeatLastHotkey
    {
        get => repeatLastHotkey;
        private set => SetShortcutField(ref repeatLastHotkey, value, nameof(RepeatLastHotkey), nameof(RepeatLastHotkeyDisplay));
    }

    public string RepeatLastHotkeyDisplay => DisplayShortcut(RepeatLastHotkey);

    public string ShortcutRecorderStatus
    {
        get => shortcutRecorderStatus;
        private set => SetLocal(ref shortcutRecorderStatus, value);
    }

    public string Palette1
    {
        get => palette1;
        set => SetPalette(0, ref palette1, value, nameof(Palette1));
    }

    public string Palette2
    {
        get => palette2;
        set => SetPalette(1, ref palette2, value, nameof(Palette2));
    }

    public string Palette3
    {
        get => palette3;
        set => SetPalette(2, ref palette3, value, nameof(Palette3));
    }

    public string Palette4
    {
        get => palette4;
        set => SetPalette(3, ref palette4, value, nameof(Palette4));
    }

    public string Palette5
    {
        get => palette5;
        set => SetPalette(4, ref palette5, value, nameof(Palette5));
    }

    public double StrokeWidth
    {
        get => strokeWidth;
        set => SetDebounced(ref strokeWidth, value, settings => settings.StrokeWidth = StrokeWidth);
    }

    public double TextSize
    {
        get => textSize;
        set => SetDebounced(ref textSize, value, settings => settings.TextSize = TextSize);
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
        set => SetDebounced(ref canvasExtensionBackgroundHex, NormalizeHex(value, canvasExtensionBackgroundHex), settings => settings.CanvasExtensionBackgroundHex = CanvasExtensionBackgroundHex);
    }

    public string SaveFolder
    {
        get => saveFolder;
        set => SetDebounced(ref saveFolder, value, settings => settings.SaveFolder = string.IsNullOrWhiteSpace(SaveFolder) ? null : SaveFolder);
    }

    public string FilenamePattern
    {
        get => filenamePattern;
        set => SetDebounced(ref filenamePattern, value, settings => settings.FilenamePattern = FilenamePattern);
    }

    public int ExportScale
    {
        get => exportScale;
        set
        {
            var normalized = value == 1 ? 1 : 2;
            if (SetAndPersist(ref exportScale, normalized, settings => settings.ExportScale = normalized))
            {
                OnPropertyChanged(nameof(IsExportScale1x));
                OnPropertyChanged(nameof(IsExportScale2x));
            }
        }
    }

    public bool IsExportScale1x
    {
        get => ExportScale == 1;
        set { if (value) ExportScale = 1; }
    }

    public bool IsExportScale2x
    {
        get => ExportScale == 2;
        set { if (value) ExportScale = 2; }
    }

    public bool NotifyWhenCopied
    {
        get => notifyWhenCopied;
        set => SetAndPersist(ref notifyWhenCopied, value, settings => settings.SuppressCopyNotification = !value);
    }

    public string? LastError
    {
        get => lastError;
        private set => SetLocal(ref lastError, value);
    }

    public string VersionText => typeof(Settings).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public string BuildChannel => "local";

    public string SourceLink => "https://github.com/kubre/scrcap";

    public bool RecordShortcut(AppAction action, WpfKey key, WpfModifierKeys modifiers)
    {
        if (key is WpfKey.Escape)
        {
            ShortcutRecorderStatus = "Shortcut recording cancelled.";
            return true;
        }

        if (IsClearKey(key))
        {
            return PersistHotkeys(action, string.Empty, null, $"{action.Title()} shortcut cleared.");
        }

        if (!TryBuildChord(key, modifiers, out var chord))
        {
            ShortcutRecorderStatus = "Use Ctrl, Alt, Shift, or Win with a letter, number, function, or navigation key.";
            return false;
        }

        if (!Keymap.IsUsableGlobalShortcut(chord))
        {
            ShortcutRecorderStatus = $"{chord.WindowsDisplayValue} is reserved by Windows.";
            return false;
        }

        AppAction? conflictedAction = null;
        foreach (var (candidateAction, existingChord) in CurrentHotkeys())
        {
            if (candidateAction != action && existingChord == chord)
            {
                conflictedAction = candidateAction;
                break;
            }
        }

        var status = conflictedAction is null
            ? $"{action.Title()} set to {chord.WindowsDisplayValue}."
            : $"{action.Title()} set to {chord.WindowsDisplayValue}; {conflictedAction.Value.Title()} was cleared.";
        return PersistHotkeys(action, chord.StringValue, conflictedAction, status);
    }

    public void BeginShortcutRecording(AppAction action)
    {
        ShortcutRecorderStatus = $"Press a shortcut for {action.Title()}. Esc cancels; Delete clears.";
    }

    public void ResetPalette()
    {
        SetAllPalette(Settings.DefaultPalette);
        OnPropertyChanged(nameof(Palette1));
        OnPropertyChanged(nameof(Palette2));
        OnPropertyChanged(nameof(Palette3));
        OnPropertyChanged(nameof(Palette4));
        OnPropertyChanged(nameof(Palette5));
        PersistImmediate(settings => settings.PaletteHex = Settings.DefaultPalette.ToList());
    }

    public void ResetAll()
    {
        FlushPendingWrites();
        var defaults = Settings.Defaults();
        if (store.Update(settings => CopySettings(defaults, settings)))
        {
            if (!launchAtLoginService.SetEnabled(defaults.LaunchAtLogin))
            {
                SetError("Could not update Launch at Login while resetting preferences.");
            }

            Load(store.Settings);
            OnPropertyChanged(string.Empty);
            ThemeModeChanged?.Invoke(this, SelectedThemeMode);
            return;
        }

        SetError("Could not reset preferences. Your previous settings file was left in place.");
    }

    public void FlushPendingWrites()
    {
        debounceTimer.Stop();
        if (pendingWrites.Count == 0)
        {
            return;
        }

        var writes = pendingWrites.Values.ToArray();
        pendingWrites.Clear();
        PersistImmediate(settings =>
        {
            foreach (var write in writes)
            {
                write(settings);
            }
        });
    }

    private void Load(Settings settings)
    {
        selectedThemeMode = settings.ThemeMode;
        launchAtLogin = launchAtLoginService.IsEnabled();
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
        SetAllPalette(settings.PaletteHex);
        strokeWidth = settings.StrokeWidth;
        textSize = settings.TextSize;
        textEnterBehavior = settings.TextEnterBehavior;
        escBehavior = settings.EscBehavior;
        autoExpandCanvas = settings.AutoExpandCanvas;
        canvasExtensionBackgroundHex = settings.CanvasExtensionBackgroundHex;
        saveFolder = settings.SaveFolder ?? string.Empty;
        filenamePattern = settings.FilenamePattern;
        exportScale = settings.ExportScale;
        notifyWhenCopied = !settings.SuppressCopyNotification;
    }

    private bool SetAndPersist<T>(ref T field, T value, Action<Settings> mutate, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        if (!PersistImmediate(mutate))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetDebounced<T>(ref T field, T value, Action<Settings> mutate, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        pendingWrites[propertyName ?? string.Empty] = mutate;
        debounceTimer.Stop();
        debounceTimer.Start();
    }

    private bool PersistImmediate(Action<Settings> mutate)
    {
        if (store.Update(mutate))
        {
            LastError = null;
            return true;
        }

        Load(store.Settings);
        OnPropertyChanged(string.Empty);
        SetError("Could not save preferences. The previous valid settings file was kept.");
        return false;
    }

    private void SetLocal<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
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

    private void SetPalette(int index, ref string field, string value, string propertyName)
    {
        var normalized = NormalizeHex(value, field);
        SetDebounced(ref field, normalized, settings => settings.PaletteHex = CurrentPaletteWith(index, normalized), propertyName);
    }

    private void SetAllPalette(IReadOnlyList<string> palette)
    {
        palette1 = palette.ElementAtOrDefault(0) ?? Settings.DefaultPalette[0];
        palette2 = palette.ElementAtOrDefault(1) ?? Settings.DefaultPalette[1];
        palette3 = palette.ElementAtOrDefault(2) ?? Settings.DefaultPalette[2];
        palette4 = palette.ElementAtOrDefault(3) ?? Settings.DefaultPalette[3];
        palette5 = palette.ElementAtOrDefault(4) ?? Settings.DefaultPalette[4];
    }

    private List<string> CurrentPaletteWith(int index, string value)
    {
        var palette = new List<string> { Palette1, Palette2, Palette3, Palette4, Palette5 };
        palette[index] = value;
        return palette;
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

        SetHotkeyLocal(action, value);
        ShortcutRecorderStatus = status;
        LastError = null;
        return true;
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

    private string GetHotkey(AppAction action) =>
        action switch
        {
            AppAction.CaptureRegion => CaptureRegionHotkey,
            AppAction.CaptureWindow => CaptureWindowHotkey,
            AppAction.CaptureFullscreen => CaptureFullscreenHotkey,
            AppAction.CaptureScrolling => CaptureScrollingHotkey,
            AppAction.CaptureDelayed => CaptureDelayedHotkey,
            AppAction.RepeatLast => RepeatLastHotkey,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    private void SetHotkeyLocal(AppAction action, string value)
    {
        switch (action)
        {
            case AppAction.CaptureRegion:
                CaptureRegionHotkey = value;
                break;
            case AppAction.CaptureWindow:
                CaptureWindowHotkey = value;
                break;
            case AppAction.CaptureFullscreen:
                CaptureFullscreenHotkey = value;
                break;
            case AppAction.CaptureScrolling:
                CaptureScrollingHotkey = value;
                break;
            case AppAction.CaptureDelayed:
                CaptureDelayedHotkey = value;
                break;
            case AppAction.RepeatLast:
                RepeatLastHotkey = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

    private static void SetAfterCapture(Settings settings, CoreCaptureMode mode, AfterCaptureBehavior behavior)
    {
        settings.AfterCapture[mode.StorageKey()] = behavior;
    }

    private static string NormalizeHex(string value, string fallback) =>
        Settings.NormalizeHexColor(value) ?? Settings.NormalizeHexColor(fallback) ?? "#FFFFFF";

    private static string DisplayShortcut(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Not set"
            : KeyChord.TryParse(value, out var chord)
                ? chord.WindowsDisplayValue
                : "Not set";

    private static bool TryBuildChord(WpfKey key, WpfModifierKeys modifiers, out KeyChord chord)
    {
        chord = default;
        if (IsModifierKey(key) || !TryKeyName(key, out var keyName))
        {
            return false;
        }

        var chordModifiers = ChordModifiers.None;
        if (modifiers.HasFlag(WpfModifierKeys.Control))
        {
            chordModifiers |= ChordModifiers.Control;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Alt))
        {
            chordModifiers |= ChordModifiers.Option;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Shift))
        {
            chordModifiers |= ChordModifiers.Shift;
        }

        if (modifiers.HasFlag(WpfModifierKeys.Windows))
        {
            chordModifiers |= ChordModifiers.Command;
        }

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
    }

    private void SetError(string message)
    {
        LastError = message;
    }

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
