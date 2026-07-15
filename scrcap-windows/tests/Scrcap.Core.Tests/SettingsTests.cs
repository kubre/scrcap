using System.Text.Json;
using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void SettingsStoreRaisesChangedOnlyAfterSuccessfulAtomicUpdate()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var changes = 0;
        store.SettingsChanged += (_, _) => changes++;

        Assert.True(store.Update(settings => settings.ThemeMode = ThemeMode.Dark));

        Assert.Equal(1, changes);
        Assert.Equal(ThemeMode.Dark, store.Settings.ThemeMode);
    }

    [Fact]
    public void DefaultsMatchCurrentHotkeyContract()
    {
        var settings = Settings.Defaults();

        Assert.Equal("opt+shift+1", settings.Hotkeys["captureRegion"]);
        Assert.Equal("opt+shift+2", settings.Hotkeys["captureWindow"]);
        Assert.Equal("opt+shift+3", settings.Hotkeys["captureFullscreen"]);
        Assert.Equal("opt+shift+4", settings.Hotkeys["captureScrolling"]);
        Assert.Equal("opt+shift+5", settings.Hotkeys["captureDelayed"]);
        Assert.Equal("opt+shift+r", settings.Hotkeys["repeatLast"]);
    }

    [Fact]
    public void NormalizeClampsUserEditableValues()
    {
        var settings = Settings.Defaults();
        settings.PaletteHex = ["red", "0a84ff"];
        settings.StrokeWidth = double.PositiveInfinity;
        settings.TextSize = 100;
        settings.ExportScale = 99;
        settings.ScrollingMaxHeight = 999_999;
        settings.CaptureDelaySeconds = -3;
        settings.CanvasExtensionBackgroundHex = "bad";
        settings.FilenamePattern = "bad/name:ok";

        settings.Normalize();

        Assert.Equal(["#FF3B30", "#0A84FF", "#34C759", "#0A84FF", "#1C1C1E"], settings.PaletteHex);
        Assert.Equal(Settings.MinStrokeWidth, settings.StrokeWidth);
        Assert.Equal(Settings.MaxTextSize, settings.TextSize);
        Assert.Equal(2, settings.ExportScale);
        Assert.Equal(Settings.MaxScrollingMaxHeight, settings.ScrollingMaxHeight);
        Assert.Equal(Settings.MinCaptureDelay, settings.CaptureDelaySeconds);
        Assert.Equal("#FFFFFF", settings.CanvasExtensionBackgroundHex);
        Assert.Equal("bad-name-ok", settings.FilenamePattern);
    }

    [Fact]
    public void StoreFallsBackToDefaultsForCorruptSettings()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), "{ nope");

        var store = new SettingsStore(temp.Path);

        Assert.Equal(Settings.CurrentSchemaVersion, store.Settings.SchemaVersion);
        Assert.Equal("opt+shift+1", store.Settings.Hotkeys["captureRegion"]);
    }

    [Fact]
    public void StoreDoesNotOverwriteCorruptSettingsOnPlainSave()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ nope");

        var store = new SettingsStore(temp.Path);

        Assert.False(store.Save());
        Assert.Equal("{ nope", File.ReadAllText(path));
    }

    [Fact]
    public void StoreOverwritesCorruptSettingsAfterUserUpdate()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(path, "{ nope");

        var store = new SettingsStore(temp.Path);
        var saved = store.Update(settings => settings.FilenamePattern = "updated-{date}");

        Assert.True(saved);
        Assert.Contains("updated-{date}", File.ReadAllText(path));
    }

    [Fact]
    public void StoreMigratesVersionSixSettings()
    {
        using var temp = new TempDirectory();
        var old = new
        {
            schemaVersion = 6,
            hotkeys = new Dictionary<string, string>
            {
                ["captureRegion"] = "opt+shift+1",
                ["captureWindow"] = "opt+shift+2",
                ["captureFullscreen"] = "opt+shift+3",
                ["captureScrolling"] = "opt+shift+4",
                ["repeatLast"] = "opt+shift+r",
            },
            afterCapture = new Dictionary<string, string>
            {
                ["region"] = "openEditor",
                ["window"] = "openEditor",
                ["fullscreen"] = "openEditor",
                ["scrolling"] = "openEditor",
            },
            paletteHex = new[] { "#FF3B30", "#FF9500", "#34C759", "#0A84FF", "#1C1C1E" },
            escBehavior = "copyAndClose",
            strokeWidth = 3,
            textSize = 16,
            textEnterBehavior = "newline",
            windowCaptureTarget = "active",
            includeWindowShadow = false,
            autoExpandCanvas = true,
            canvasExtensionBackgroundHex = "#FFFFFF",
            saveFolder = (string?)null,
            filenamePattern = "scrcap-{date}-{time}",
            exportScale = 2,
            scrollingMaxHeight = 20_000,
            launchAtLogin = false,
            themeMode = "system",
        };
        File.WriteAllText(Path.Combine(temp.Path, "settings.json"), JsonSerializer.Serialize(old));

        var store = new SettingsStore(temp.Path);

        Assert.Equal(Settings.CurrentSchemaVersion, store.Settings.SchemaVersion);
        Assert.Equal("opt+shift+5", store.Settings.Hotkeys["captureDelayed"]);
        Assert.False(store.Settings.IncludeCursor);
        Assert.False(store.Settings.WindowBackgroundTransparent);
        Assert.Equal("#FFFFFF", store.Settings.WindowBackgroundHex);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
