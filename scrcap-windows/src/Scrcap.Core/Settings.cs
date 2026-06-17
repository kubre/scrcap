using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Scrcap.Core;

public enum CaptureMode
{
    Region,
    Window,
    Fullscreen,
    Scrolling,
}

public enum AfterCaptureBehavior
{
    OpenEditor,
    CopyOnly,
    Both,
}

public enum EscBehavior
{
    CopyAndClose,
    CloseOnly,
}

public enum TextEnterBehavior
{
    Newline,
    Commit,
}

public enum WindowCaptureTarget
{
    Active,
    Selected,
}

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public sealed class Settings
{
    public const int CurrentSchemaVersion = 8;
    public const int PaletteSlotCount = 5;
    public const double MinStrokeWidth = 1.0;
    public const double MaxStrokeWidth = 8.0;
    public const double MinTextSize = 10.0;
    public const double MaxTextSize = 36.0;
    public const int MinScrollingMaxHeight = 1_000;
    public const int MaxScrollingMaxHeight = 100_000;
    public const int MinCaptureDelay = 1;
    public const int MaxCaptureDelay = 10;

    public static IReadOnlyList<string> DefaultPalette { get; } =
    [
        "#FF3B30",
        "#FF9500",
        "#34C759",
        "#0A84FF",
        "#1C1C1E",
    ];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<string, string> Hotkeys { get; set; } = [];

    public Dictionary<string, AfterCaptureBehavior> AfterCapture { get; set; } = [];

    public List<string> PaletteHex { get; set; } = [];

    public EscBehavior EscBehavior { get; set; } = EscBehavior.CopyAndClose;

    public double StrokeWidth { get; set; } = 3;

    public double TextSize { get; set; } = 16;

    public TextEnterBehavior TextEnterBehavior { get; set; } = TextEnterBehavior.Newline;

    public WindowCaptureTarget WindowCaptureTarget { get; set; } = WindowCaptureTarget.Active;

    public bool IncludeWindowShadow { get; set; }

    public bool WindowBackgroundTransparent { get; set; }

    public string WindowBackgroundHex { get; set; } = "#FFFFFF";

    public bool AutoExpandCanvas { get; set; } = true;

    public string CanvasExtensionBackgroundHex { get; set; } = "#FFFFFF";

    public string? SaveFolder { get; set; }

    public string FilenamePattern { get; set; } = "scrcap-{date}-{time}";

    public int ExportScale { get; set; } = 2;

    public int ScrollingMaxHeight { get; set; } = 20_000;

    public bool LaunchAtLogin { get; set; }

    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public bool IncludeCursor { get; set; }

    public int CaptureDelaySeconds { get; set; } = 3;

    public bool SuppressCopyNotification { get; set; }

    public int ResolvedExportScale => ExportScale == 1 ? 1 : 2;

    public static Settings Defaults()
    {
        var settings = new Settings
        {
            Hotkeys = Keymap.Defaults.Bindings.ToDictionary(pair => pair.Key.StorageKey(), pair => pair.Value.StringValue),
            AfterCapture = CaptureOrder.ToDictionary(mode => mode.StorageKey(), _ => AfterCaptureBehavior.OpenEditor),
            PaletteHex = DefaultPalette.ToList(),
        };

        settings.Normalize();
        return settings;
    }

    public static IReadOnlyList<CaptureMode> CaptureOrder { get; } =
    [
        CaptureMode.Region,
        CaptureMode.Window,
        CaptureMode.Fullscreen,
        CaptureMode.Scrolling,
    ];

    public Keymap Keymap
    {
        get
        {
            var bindings = new Dictionary<AppAction, KeyChord>();
            foreach (var (raw, chordString) in Hotkeys)
            {
                if (AppActionExtensions.TryParseStorageKey(raw, out var action)
                    && KeyChord.TryParse(chordString, out var chord))
                {
                    bindings[action] = chord;
                }
            }

            return new Keymap(bindings);
        }
    }

    public AfterCaptureBehavior BehaviorFor(CaptureMode mode) =>
        AfterCapture.TryGetValue(mode.StorageKey(), out var behavior)
            ? behavior
            : AfterCaptureBehavior.OpenEditor;

    public void Apply(Keymap keymap) =>
        Hotkeys = keymap.Bindings.ToDictionary(pair => pair.Key.StorageKey(), pair => pair.Value.StringValue);

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;
        NormalizeLegacyDefaultCaptureHotkeys();

        PaletteHex = Enumerable.Range(0, PaletteSlotCount)
            .Select(index =>
            {
                var candidate = index < PaletteHex.Count ? PaletteHex[index] : DefaultPalette[index];
                return NormalizeHexColor(candidate) ?? DefaultPalette[index];
            })
            .ToList();

        CanvasExtensionBackgroundHex = NormalizeHexColor(CanvasExtensionBackgroundHex) ?? "#FFFFFF";
        WindowBackgroundHex = NormalizeHexColor(WindowBackgroundHex) ?? "#FFFFFF";
        StrokeWidth = ClampFinite(StrokeWidth, MinStrokeWidth, MaxStrokeWidth);
        TextSize = ClampFinite(TextSize, MinTextSize, MaxTextSize);
        ExportScale = ExportScale == 1 ? 1 : 2;
        ScrollingMaxHeight = Math.Clamp(ScrollingMaxHeight, MinScrollingMaxHeight, MaxScrollingMaxHeight);
        CaptureDelaySeconds = Math.Clamp(CaptureDelaySeconds, MinCaptureDelay, MaxCaptureDelay);
        FilenamePattern = FilenameGenerator.SafeFilenameStem(FilenamePattern);
    }

    private void NormalizeLegacyDefaultCaptureHotkeys()
    {
        Hotkeys.TryGetValue(AppAction.CaptureRegion.StorageKey(), out var region);
        Hotkeys.TryGetValue(AppAction.CaptureWindow.StorageKey(), out var window);
        Hotkeys.TryGetValue(AppAction.CaptureFullscreen.StorageKey(), out var fullscreen);
        Hotkeys.TryGetValue(AppAction.CaptureScrolling.StorageKey(), out var scrolling);

        if (scrolling != "opt+shift+4")
        {
            return;
        }

        if ((region == "opt+shift+1" && window == "opt+shift+3" && fullscreen == "opt+shift+2")
            || (region == "opt+shift+2" && window == "opt+shift+3" && fullscreen == "opt+shift+1"))
        {
            Hotkeys[AppAction.CaptureRegion.StorageKey()] = "opt+shift+1";
            Hotkeys[AppAction.CaptureWindow.StorageKey()] = "opt+shift+2";
            Hotkeys[AppAction.CaptureFullscreen.StorageKey()] = "opt+shift+3";
        }
    }

    private static double ClampFinite(double value, double min, double max) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : min;

    public static string? NormalizeHexColor(string raw)
    {
        var value = raw.Trim().ToUpperInvariant();
        if (value.StartsWith('#'))
        {
            value = value[1..];
        }

        return value.Length == 6 && value.All(IsHex)
            ? "#" + value
            : null;
    }

    private static bool IsHex(char value) =>
        value is >= '0' and <= '9' or >= 'A' and <= 'F';
}

public static class CaptureModeExtensions
{
    public static string StorageKey(this CaptureMode mode) =>
        mode switch
        {
            CaptureMode.Region => "region",
            CaptureMode.Window => "window",
            CaptureMode.Fullscreen => "fullscreen",
            CaptureMode.Scrolling => "scrolling",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
}

public sealed class SettingsStore
{
    private readonly string filePath;
    private readonly bool loadedFromCorruptFile;

    public SettingsStore(string directory)
    {
        filePath = Path.Combine(directory, "settings.json");
        var loadResult = Load(filePath);
        loadedFromCorruptFile = loadResult.IsInvalid;
        Settings = loadResult.Settings ?? Settings.Defaults();
        Settings.Normalize();
    }

    public Settings Settings { get; private set; }

    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "scrcap");

    public bool Update(Action<Settings> mutate)
    {
        var next = Clone(Settings);
        mutate(next);
        next.Normalize();

        if (!Save(next, allowFallbackOverwrite: true))
        {
            return false;
        }

        Settings = next;
        return true;
    }

    public bool Save() => !loadedFromCorruptFile && Save(Settings, allowFallbackOverwrite: false);

    private bool Save(Settings settings, bool allowFallbackOverwrite)
    {
        if (loadedFromCorruptFile && !allowFallbackOverwrite)
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var tempPath = filePath + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, filePath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static LoadResult Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return LoadResult.Missing;
            }

            var migrated = Migrate(File.ReadAllText(path));
            if (migrated is null)
            {
                return LoadResult.Invalid;
            }

            return JsonSerializer.Deserialize<Settings>(migrated, JsonOptions) is { } settings
                ? LoadResult.Valid(settings)
                : LoadResult.Invalid;
        }
        catch
        {
            return LoadResult.Invalid;
        }
    }

    private static string? Migrate(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node is null
            || node["schemaVersion"] is null
            || !int.TryParse(node["schemaVersion"]!.ToString(), out var version)
            || version > Settings.CurrentSchemaVersion)
        {
            return null;
        }

        while (version < Settings.CurrentSchemaVersion)
        {
            switch (version)
            {
                case 1:
                    node["textSize"] = 16.0;
                    break;
                case 2:
                    node["textEnterBehavior"] = "newline";
                    break;
                case 3:
                    node["windowCaptureTarget"] = "active";
                    break;
                case 4:
                    node["autoExpandCanvas"] = true;
                    node["canvasExtensionBackgroundHex"] = "#FFFFFF";
                    break;
                case 5:
                    node["themeMode"] = "system";
                    break;
                case 6:
                    var palette = new JsonArray();
                    foreach (var color in Settings.DefaultPalette)
                    {
                        palette.Add(color);
                    }

                    node["paletteHex"] = palette;
                    node["includeCursor"] = false;
                    node["captureDelaySeconds"] = 3;
                    node["suppressCopyNotification"] = false;
                    if (node["hotkeys"] is JsonObject hotkeys
                        && !hotkeys.ContainsKey(AppAction.CaptureDelayed.StorageKey()))
                    {
                        hotkeys[AppAction.CaptureDelayed.StorageKey()] = "opt+shift+5";
                    }

                    break;
                case 7:
                    node["windowBackgroundTransparent"] = false;
                    node["windowBackgroundHex"] = "#FFFFFF";
                    break;
            }

            version++;
            node["schemaVersion"] = version;
        }

        return node.ToJsonString(JsonOptions);
    }

    private static Settings Clone(Settings settings) =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(settings, JsonOptions), JsonOptions)
        ?? Settings.Defaults();

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private sealed record LoadResult(Settings? Settings, bool IsInvalid)
    {
        public static LoadResult Missing { get; } = new(null, false);

        public static LoadResult Invalid { get; } = new(null, true);

        public static LoadResult Valid(Settings settings) => new(settings, false);
    }
}
