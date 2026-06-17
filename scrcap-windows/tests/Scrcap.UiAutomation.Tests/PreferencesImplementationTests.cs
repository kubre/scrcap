using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Scrcap.Core;
using Scrcap.Windows.UI.Preferences;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.UiAutomation.Tests;

public sealed class PreferencesImplementationTests
{
    [Fact]
    public void ShortcutRecorderStoresUsableChordAndClearsConflict()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var viewModel = new PreferencesViewModel(store);

        Assert.True(viewModel.RecordShortcut(AppAction.CaptureRegion, Key.D6, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal("ctrl+shift+6", viewModel.CaptureRegionHotkey);

        Assert.True(viewModel.RecordShortcut(AppAction.CaptureWindow, Key.D6, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Empty(viewModel.CaptureRegionHotkey);
        Assert.Equal("ctrl+shift+6", viewModel.CaptureWindowHotkey);
        Assert.Contains("Capture Region was cleared", viewModel.ShortcutRecorderStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortcutRecorderRejectsWindowsReservedChord()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var viewModel = new PreferencesViewModel(store);
        var original = viewModel.CaptureWindowHotkey;

        Assert.False(viewModel.RecordShortcut(AppAction.CaptureWindow, Key.F4, ModifierKeys.Alt));

        Assert.Equal(original, viewModel.CaptureWindowHotkey);
        Assert.Contains("reserved by Windows", viewModel.ShortcutRecorderStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeServiceMutatesBrushResourcesForSelectedMode()
    {
        var resources = ThemeDictionary();
        var chrome = Assert.IsType<SolidColorBrush>(resources["BrushChrome"]);

        AppThemeService.Apply(resources, ThemeMode.Dark);

        Assert.Equal(Color.FromRgb(28, 29, 32), chrome.Color);

        AppThemeService.Apply(resources, ThemeMode.Light);

        Assert.Equal(Color.FromRgb(247, 247, 248), chrome.Color);
    }

    private static ResourceDictionary ThemeDictionary()
    {
        var resources = new ResourceDictionary();
        foreach (var suffix in new[] { "Ink", "Muted", "Chrome", "Panel", "Rule", "Canvas", "Hover", "Pressed" })
        {
            resources["Color" + suffix] = Colors.White;
            resources["Brush" + suffix] = new SolidColorBrush(Colors.White);
        }

        return resources;
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
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
