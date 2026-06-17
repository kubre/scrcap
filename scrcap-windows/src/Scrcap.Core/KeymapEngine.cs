namespace Scrcap.Core;

[Flags]
public enum ChordModifiers : byte
{
    None = 0,
    Command = 1 << 0,
    Option = 1 << 1,
    Shift = 1 << 2,
    Control = 1 << 3,
}

public readonly record struct KeyChord(string Key, ChordModifiers Modifiers)
{
    public string Key { get; } = Key.ToLowerInvariant();

    public static bool TryParse(string value, out KeyChord chord)
    {
        chord = default;
        ChordModifiers modifiers = ChordModifiers.None;
        string? key = null;

        foreach (var raw in value.Split('+', StringSplitOptions.TrimEntries))
        {
            var token = raw.ToLowerInvariant();
            switch (token)
            {
                case "cmd":
                case "command":
                case "win":
                case "windows":
                case "meta":
                case "⌘":
                    modifiers |= ChordModifiers.Command;
                    break;
                case "opt":
                case "option":
                case "alt":
                case "⌥":
                    modifiers |= ChordModifiers.Option;
                    break;
                case "shift":
                case "⇧":
                    modifiers |= ChordModifiers.Shift;
                    break;
                case "ctrl":
                case "control":
                case "⌃":
                    modifiers |= ChordModifiers.Control;
                    break;
                case "":
                    break;
                default:
                    if (key is not null)
                    {
                        return false;
                    }

                    key = token;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        chord = new KeyChord(key, modifiers);
        return true;
    }

    public string StringValue
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ChordModifiers.Control))
            {
                parts.Add("ctrl");
            }

            if (Modifiers.HasFlag(ChordModifiers.Option))
            {
                parts.Add("opt");
            }

            if (Modifiers.HasFlag(ChordModifiers.Shift))
            {
                parts.Add("shift");
            }

            if (Modifiers.HasFlag(ChordModifiers.Command))
            {
                parts.Add("cmd");
            }

            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public string WindowsDisplayValue
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ChordModifiers.Control))
            {
                parts.Add("Ctrl");
            }

            if (Modifiers.HasFlag(ChordModifiers.Option))
            {
                parts.Add("Alt");
            }

            if (Modifiers.HasFlag(ChordModifiers.Shift))
            {
                parts.Add("Shift");
            }

            if (Modifiers.HasFlag(ChordModifiers.Command))
            {
                parts.Add("Win");
            }

            parts.Add(Key.ToUpperInvariant());
            return string.Join('+', parts);
        }
    }
}

public enum AppAction
{
    CaptureRegion,
    CaptureWindow,
    CaptureFullscreen,
    CaptureScrolling,
    CaptureDelayed,
    RepeatLast,
}

public static class AppActionExtensions
{
    public static IReadOnlyList<AppAction> ShortcutOrder { get; } =
    [
        AppAction.CaptureRegion,
        AppAction.CaptureWindow,
        AppAction.CaptureFullscreen,
        AppAction.CaptureScrolling,
        AppAction.CaptureDelayed,
        AppAction.RepeatLast,
    ];

    public static string StorageKey(this AppAction action) =>
        action switch
        {
            AppAction.CaptureRegion => "captureRegion",
            AppAction.CaptureWindow => "captureWindow",
            AppAction.CaptureFullscreen => "captureFullscreen",
            AppAction.CaptureScrolling => "captureScrolling",
            AppAction.CaptureDelayed => "captureDelayed",
            AppAction.RepeatLast => "repeatLast",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

    public static bool TryParseStorageKey(string value, out AppAction action)
    {
        action = value switch
        {
            "captureRegion" => AppAction.CaptureRegion,
            "captureWindow" => AppAction.CaptureWindow,
            "captureFullscreen" => AppAction.CaptureFullscreen,
            "captureScrolling" => AppAction.CaptureScrolling,
            "captureDelayed" => AppAction.CaptureDelayed,
            "repeatLast" => AppAction.RepeatLast,
            _ => default,
        };

        return value is "captureRegion" or "captureWindow" or "captureFullscreen" or "captureScrolling" or "captureDelayed" or "repeatLast";
    }

    public static string Title(this AppAction action) =>
        action switch
        {
            AppAction.CaptureFullscreen => "Capture Fullscreen",
            AppAction.CaptureRegion => "Capture Region",
            AppAction.CaptureWindow => "Capture Window",
            AppAction.CaptureScrolling => "Scrolling Capture",
            AppAction.CaptureDelayed => "Delayed Region Capture",
            AppAction.RepeatLast => "Repeat Last Capture",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };
}

public sealed class Keymap
{
    public Keymap(IDictionary<AppAction, KeyChord> bindings)
    {
        Bindings = new Dictionary<AppAction, KeyChord>(bindings);
    }

    public static Keymap Defaults { get; } = new(new Dictionary<AppAction, KeyChord>
    {
        [AppAction.CaptureRegion] = new("1", ChordModifiers.Option | ChordModifiers.Shift),
        [AppAction.CaptureWindow] = new("2", ChordModifiers.Option | ChordModifiers.Shift),
        [AppAction.CaptureFullscreen] = new("3", ChordModifiers.Option | ChordModifiers.Shift),
        [AppAction.CaptureScrolling] = new("4", ChordModifiers.Option | ChordModifiers.Shift),
        [AppAction.CaptureDelayed] = new("5", ChordModifiers.Option | ChordModifiers.Shift),
        [AppAction.RepeatLast] = new("r", ChordModifiers.Option | ChordModifiers.Shift),
    });

    public Dictionary<AppAction, KeyChord> Bindings { get; }

    public KeyChord? ChordFor(AppAction action) =>
        Bindings.TryGetValue(action, out var chord) ? chord : null;

    public AppAction? ConflictFor(KeyChord chord, AppAction? excluding = null)
    {
        foreach (var (action, existing) in Bindings)
        {
            if (existing == chord && action != excluding)
            {
                return action;
            }
        }

        return null;
    }

    public AppAction? Set(KeyChord chord, AppAction action)
    {
        var conflicted = ConflictFor(chord, action);
        if (conflicted is { } victim)
        {
            Bindings.Remove(victim);
        }

        Bindings[action] = chord;
        return conflicted;
    }

    public static IReadOnlySet<KeyChord> SystemReserved { get; } = new HashSet<KeyChord>
    {
        new("l", ChordModifiers.Command),
        new("d", ChordModifiers.Command),
        new("tab", ChordModifiers.Command),
        new("f4", ChordModifiers.Option),
        new("delete", ChordModifiers.Control | ChordModifiers.Option),
    };

    public static bool IsSystemReserved(KeyChord chord) => SystemReserved.Contains(chord);

    public static bool IsUsableGlobalShortcut(KeyChord chord) =>
        chord.Modifiers != ChordModifiers.None && !IsSystemReserved(chord);
}
