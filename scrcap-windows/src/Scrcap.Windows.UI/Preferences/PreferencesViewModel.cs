using System.ComponentModel;
using System.Runtime.CompilerServices;
using Scrcap.Core;
using CoreCaptureMode = Scrcap.Core.CaptureMode;
using WpfKey = System.Windows.Input.Key;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace Scrcap.Windows.UI.Preferences;

public sealed class PreferencesViewModel : INotifyPropertyChanged
{
    private readonly SettingsStore store;

    public PreferencesViewModel(SettingsStore store)
    {
        this.store = store;
        Load(store.Settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<ThemeMode> ThemeModes { get; } = [ThemeMode.System, ThemeMode.Light, ThemeMode.Dark];

    public IReadOnlyList<AfterCaptureBehavior> CaptureBehaviors { get; } = [AfterCaptureBehavior.OpenEditor, AfterCaptureBehavior.CopyOnly, AfterCaptureBehavior.Both];

    public IReadOnlyList<WindowCaptureTarget> WindowTargets { get; } = [WindowCaptureTarget.Active, WindowCaptureTarget.Selected];

    public IReadOnlyList<TextEnterBehavior> TextEnterBehaviors { get; } = [TextEnterBehavior.Newline, TextEnterBehavior.Commit];

    public IReadOnlyList<EscBehavior> EscBehaviors { get; } = [EscBehavior.CopyAndClose, EscBehavior.CloseOnly];

    public IReadOnlyList<int> ExportScales { get; } = [1, 2];

    public ThemeMode SelectedThemeMode { get; set; }

    public bool LaunchAtLogin { get; set; }

    public bool IncludeCursor { get; set; }

    public bool IncludeWindowShadow { get; set; }

    public bool WindowBackgroundTransparent { get; set; }

    public string WindowBackgroundHex { get; set; } = "#FFFFFF";

    public int CaptureDelaySeconds { get; set; }

    public int ScrollingMaxHeight { get; set; }

    public WindowCaptureTarget WindowCaptureTarget { get; set; }

    public AfterCaptureBehavior RegionAfterCapture { get; set; }

    public AfterCaptureBehavior WindowAfterCapture { get; set; }

    public AfterCaptureBehavior FullscreenAfterCapture { get; set; }

    public AfterCaptureBehavior ScrollingAfterCapture { get; set; }

    public string CaptureRegionHotkey { get; set; } = string.Empty;

    public string CaptureWindowHotkey { get; set; } = string.Empty;

    public string CaptureFullscreenHotkey { get; set; } = string.Empty;

    public string CaptureScrollingHotkey { get; set; } = string.Empty;

    public string CaptureDelayedHotkey { get; set; } = string.Empty;

    public string RepeatLastHotkey { get; set; } = string.Empty;

    public string ShortcutRecorderStatus { get; private set; } = "Press a shortcut while focused in a shortcut field.";

    public string Palette1 { get; set; } = string.Empty;

    public string Palette2 { get; set; } = string.Empty;

    public string Palette3 { get; set; } = string.Empty;

    public string Palette4 { get; set; } = string.Empty;

    public string Palette5 { get; set; } = string.Empty;

    public double StrokeWidth { get; set; }

    public double TextSize { get; set; }

    public TextEnterBehavior TextEnterBehavior { get; set; }

    public EscBehavior EscBehavior { get; set; }

    public bool AutoExpandCanvas { get; set; }

    public string CanvasExtensionBackgroundHex { get; set; } = "#FFFFFF";

    public string SaveFolder { get; set; } = string.Empty;

    public string FilenamePattern { get; set; } = string.Empty;

    public int ExportScale { get; set; }

    public bool SuppressCopyNotification { get; set; }

    public string VersionText => typeof(Settings).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    public string BuildChannel => "local";

    public string SourceLink => "https://github.com/";

    public bool RecordShortcut(AppAction action, WpfKey key, WpfModifierKeys modifiers)
    {
        if (IsClearKey(key))
        {
            SetHotkey(action, string.Empty);
            ShortcutRecorderStatus = $"{action.Title()} shortcut cleared.";
            OnPropertyChanged(nameof(ShortcutRecorderStatus));
            return true;
        }

        if (!TryBuildChord(key, modifiers, out var chord))
        {
            ShortcutRecorderStatus = "Use at least one modifier plus a letter, number, function, or navigation key.";
            OnPropertyChanged(nameof(ShortcutRecorderStatus));
            return false;
        }

        if (!Keymap.IsUsableGlobalShortcut(chord))
        {
            ShortcutRecorderStatus = $"{chord.WindowsDisplayValue} is reserved by Windows.";
            OnPropertyChanged(nameof(ShortcutRecorderStatus));
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

        if (conflictedAction is { } victim)
        {
            SetHotkey(victim, string.Empty);
        }

        SetHotkey(action, chord.StringValue);
        ShortcutRecorderStatus = conflictedAction is null
            ? $"{action.Title()} set to {chord.WindowsDisplayValue}."
            : $"{action.Title()} set to {chord.WindowsDisplayValue}; {conflictedAction.Value.Title()} was cleared.";
        OnPropertyChanged(nameof(ShortcutRecorderStatus));
        return true;
    }

    public void ResetAll()
    {
        Load(Settings.Defaults());
        OnPropertyChanged(string.Empty);
    }

    public bool Save() =>
        store.Update(settings =>
        {
            settings.ThemeMode = SelectedThemeMode;
            settings.LaunchAtLogin = LaunchAtLogin;
            settings.IncludeCursor = IncludeCursor;
            settings.IncludeWindowShadow = IncludeWindowShadow;
            settings.WindowBackgroundTransparent = WindowBackgroundTransparent;
            settings.WindowBackgroundHex = WindowBackgroundHex;
            settings.CaptureDelaySeconds = CaptureDelaySeconds;
            settings.ScrollingMaxHeight = ScrollingMaxHeight;
            settings.WindowCaptureTarget = WindowCaptureTarget;
            settings.AfterCapture = new Dictionary<string, AfterCaptureBehavior>
            {
                [CoreCaptureMode.Region.StorageKey()] = RegionAfterCapture,
                [CoreCaptureMode.Window.StorageKey()] = WindowAfterCapture,
                [CoreCaptureMode.Fullscreen.StorageKey()] = FullscreenAfterCapture,
                [CoreCaptureMode.Scrolling.StorageKey()] = ScrollingAfterCapture,
            };
            settings.Hotkeys = new Dictionary<string, string>
            {
                [AppAction.CaptureRegion.StorageKey()] = CaptureRegionHotkey,
                [AppAction.CaptureWindow.StorageKey()] = CaptureWindowHotkey,
                [AppAction.CaptureFullscreen.StorageKey()] = CaptureFullscreenHotkey,
                [AppAction.CaptureScrolling.StorageKey()] = CaptureScrollingHotkey,
                [AppAction.CaptureDelayed.StorageKey()] = CaptureDelayedHotkey,
                [AppAction.RepeatLast.StorageKey()] = RepeatLastHotkey,
            };
            settings.PaletteHex = [Palette1, Palette2, Palette3, Palette4, Palette5];
            settings.StrokeWidth = StrokeWidth;
            settings.TextSize = TextSize;
            settings.TextEnterBehavior = TextEnterBehavior;
            settings.EscBehavior = EscBehavior;
            settings.AutoExpandCanvas = AutoExpandCanvas;
            settings.CanvasExtensionBackgroundHex = CanvasExtensionBackgroundHex;
            settings.SaveFolder = string.IsNullOrWhiteSpace(SaveFolder) ? null : SaveFolder;
            settings.FilenamePattern = FilenamePattern;
            settings.ExportScale = ExportScale;
            settings.SuppressCopyNotification = SuppressCopyNotification;
        });

    private void Load(Settings settings)
    {
        SelectedThemeMode = settings.ThemeMode;
        LaunchAtLogin = settings.LaunchAtLogin;
        IncludeCursor = settings.IncludeCursor;
        IncludeWindowShadow = settings.IncludeWindowShadow;
        WindowBackgroundTransparent = settings.WindowBackgroundTransparent;
        WindowBackgroundHex = settings.WindowBackgroundHex;
        CaptureDelaySeconds = settings.CaptureDelaySeconds;
        ScrollingMaxHeight = settings.ScrollingMaxHeight;
        WindowCaptureTarget = settings.WindowCaptureTarget;
        RegionAfterCapture = settings.BehaviorFor(CoreCaptureMode.Region);
        WindowAfterCapture = settings.BehaviorFor(CoreCaptureMode.Window);
        FullscreenAfterCapture = settings.BehaviorFor(CoreCaptureMode.Fullscreen);
        ScrollingAfterCapture = settings.BehaviorFor(CoreCaptureMode.Scrolling);
        CaptureRegionHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureRegion.StorageKey(), string.Empty);
        CaptureWindowHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureWindow.StorageKey(), string.Empty);
        CaptureFullscreenHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureFullscreen.StorageKey(), string.Empty);
        CaptureScrollingHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureScrolling.StorageKey(), string.Empty);
        CaptureDelayedHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.CaptureDelayed.StorageKey(), string.Empty);
        RepeatLastHotkey = settings.Hotkeys.GetValueOrDefault(AppAction.RepeatLast.StorageKey(), string.Empty);
        Palette1 = settings.PaletteHex.ElementAtOrDefault(0) ?? Settings.DefaultPalette[0];
        Palette2 = settings.PaletteHex.ElementAtOrDefault(1) ?? Settings.DefaultPalette[1];
        Palette3 = settings.PaletteHex.ElementAtOrDefault(2) ?? Settings.DefaultPalette[2];
        Palette4 = settings.PaletteHex.ElementAtOrDefault(3) ?? Settings.DefaultPalette[3];
        Palette5 = settings.PaletteHex.ElementAtOrDefault(4) ?? Settings.DefaultPalette[4];
        StrokeWidth = settings.StrokeWidth;
        TextSize = settings.TextSize;
        TextEnterBehavior = settings.TextEnterBehavior;
        EscBehavior = settings.EscBehavior;
        AutoExpandCanvas = settings.AutoExpandCanvas;
        CanvasExtensionBackgroundHex = settings.CanvasExtensionBackgroundHex;
        SaveFolder = settings.SaveFolder ?? string.Empty;
        FilenamePattern = settings.FilenamePattern;
        ExportScale = settings.ExportScale;
        SuppressCopyNotification = settings.SuppressCopyNotification;
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

    private void SetHotkey(AppAction action, string value)
    {
        switch (action)
        {
            case AppAction.CaptureRegion:
                CaptureRegionHotkey = value;
                OnPropertyChanged(nameof(CaptureRegionHotkey));
                break;
            case AppAction.CaptureWindow:
                CaptureWindowHotkey = value;
                OnPropertyChanged(nameof(CaptureWindowHotkey));
                break;
            case AppAction.CaptureFullscreen:
                CaptureFullscreenHotkey = value;
                OnPropertyChanged(nameof(CaptureFullscreenHotkey));
                break;
            case AppAction.CaptureScrolling:
                CaptureScrollingHotkey = value;
                OnPropertyChanged(nameof(CaptureScrollingHotkey));
                break;
            case AppAction.CaptureDelayed:
                CaptureDelayedHotkey = value;
                OnPropertyChanged(nameof(CaptureDelayedHotkey));
                break;
            case AppAction.RepeatLast:
                RepeatLastHotkey = value;
                OnPropertyChanged(nameof(RepeatLastHotkey));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), action, null);
        }
    }

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
            WpfKey.Escape => "escape",
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

    public void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
