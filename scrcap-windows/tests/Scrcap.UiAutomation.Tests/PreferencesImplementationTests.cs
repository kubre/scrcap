using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scrcap.Core;
using Scrcap.Windows.UI.Preferences;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.UiAutomation.Tests;

public sealed class PreferencesImplementationTests
{
    [Fact]
    public void PreferencesTabsExposeAutomationTargetsForEveryTabAndEditableSetting()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Preferences/PreferencesWindow.xaml");

        foreach (var tab in new[] { "General", "Capture", "Shortcuts", "Editor", "Output", "About" })
        {
            Assert.Contains($"Header=\"{tab}\"", xaml, StringComparison.Ordinal);
        }

        foreach (var automationId in new[]
                 {
                     "PreferencesTabs",
                     "ThemeMode",
                     "LaunchAtLogin",
                     "RegionAfterCapture",
                     "WindowCaptureTarget",
                     "IncludeCursor",
                     "CaptureDelaySeconds",
                     "CaptureRegionHotkey",
                     "StrokeWidth",
                     "TextSize",
                     "AutoExpandCanvas",
                     "FilenamePattern",
                     "ExportScale",
                     "SuppressCopyNotification",
                     "ResetAllPreferences",
                     "SavePreferences",
                     "ClosePreferences",
                 })
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"{automationId}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreferencesWindowSelectsEveryTab()
    {
        RunSta(() =>
        {
            EnsureApplication();
            using var temp = new TempDirectory();
            var window = new PreferencesWindow(new SettingsStore(temp.Path))
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
            };

            window.Show();
            window.UpdateLayout();
            var tabs = Assert.IsType<TabControl>(FindVisualChild<TabControl>(window));
            Assert.Equal(6, tabs.Items.Count);

            for (var index = 0; index < tabs.Items.Count; index++)
            {
                tabs.SelectedIndex = index;
                tabs.UpdateLayout();
                var item = Assert.IsType<TabItem>(tabs.Items[index]);
                Assert.True(item.IsSelected);
                Assert.False(string.IsNullOrWhiteSpace(item.Header?.ToString()));
            }

            window.Close();
        });
    }

    [Fact]
    public void PreferencesSaveWritesJsonAndReopenedStoreLoadsChangedSettingPerEditableTab()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var viewModel = new PreferencesViewModel(store)
        {
            SelectedThemeMode = ThemeMode.Dark,
            RegionAfterCapture = AfterCaptureBehavior.Both,
            CaptureDelaySeconds = 7,
            StrokeWidth = 6,
            TextSize = 28,
            AutoExpandCanvas = false,
            SaveFolder = temp.Path,
            FilenamePattern = "prefs-persist-{date}",
            ExportScale = 1,
            SuppressCopyNotification = true,
        };

        Assert.True(viewModel.RecordShortcut(AppAction.CaptureDelayed, Key.D7, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(viewModel.Save());

        var settingsPath = System.IO.Path.Combine(temp.Path, "settings.json");
        Assert.True(File.Exists(settingsPath));
        var json = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
        Assert.Equal("dark", json["themeMode"]!.GetValue<string>());
        Assert.Equal(7, json["captureDelaySeconds"]!.GetValue<int>());
        Assert.Equal(6, json["strokeWidth"]!.GetValue<double>());
        Assert.Equal("prefs-persist-{date}", json["filenamePattern"]!.GetValue<string>());
        Assert.Equal("ctrl+shift+7", json["hotkeys"]![AppAction.CaptureDelayed.StorageKey()]!.GetValue<string>());
        Assert.Equal("both", json["afterCapture"]![Scrcap.Core.CaptureMode.Region.StorageKey()]!.GetValue<string>());

        var reopened = new PreferencesViewModel(new SettingsStore(temp.Path));
        Assert.Equal(ThemeMode.Dark, reopened.SelectedThemeMode);
        Assert.Equal(AfterCaptureBehavior.Both, reopened.RegionAfterCapture);
        Assert.Equal(7, reopened.CaptureDelaySeconds);
        Assert.Equal("ctrl+shift+7", reopened.CaptureDelayedHotkey);
        Assert.Equal(6, reopened.StrokeWidth);
        Assert.False(reopened.AutoExpandCanvas);
        Assert.Equal("prefs-persist-{date}", reopened.FilenamePattern);
        Assert.Equal(1, reopened.ExportScale);
        Assert.True(reopened.SuppressCopyNotification);
    }

    [Fact]
    public void PreferencesResetDefaultsRestoresEditableTabsAndPersistsDefaults()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.Path);
        var viewModel = new PreferencesViewModel(store)
        {
            SelectedThemeMode = ThemeMode.Dark,
            IncludeCursor = true,
            CaptureDelaySeconds = 9,
            Palette1 = "#010203",
            StrokeWidth = 8,
            FilenamePattern = "changed",
            ExportScale = 1,
        };
        Assert.True(viewModel.RecordShortcut(AppAction.CaptureRegion, Key.D8, ModifierKeys.Control | ModifierKeys.Shift));

        viewModel.ResetAll();
        Assert.True(viewModel.Save());

        var defaults = Settings.Defaults();
        var reopened = new PreferencesViewModel(new SettingsStore(temp.Path));
        Assert.Equal(defaults.ThemeMode, reopened.SelectedThemeMode);
        Assert.Equal(defaults.IncludeCursor, reopened.IncludeCursor);
        Assert.Equal(defaults.CaptureDelaySeconds, reopened.CaptureDelaySeconds);
        Assert.Equal(defaults.PaletteHex[0], reopened.Palette1);
        Assert.Equal(defaults.StrokeWidth, reopened.StrokeWidth);
        Assert.Equal(defaults.FilenamePattern, reopened.FilenamePattern);
        Assert.Equal(defaults.ExportScale, reopened.ExportScale);
        Assert.Equal(defaults.Hotkeys[AppAction.CaptureRegion.StorageKey()], reopened.CaptureRegionHotkey);
    }

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

        Assert.Equal(Color.FromRgb(27, 27, 28), chrome.Color);

        AppThemeService.Apply(resources, ThemeMode.Light);

        Assert.Equal(Color.FromRgb(242, 242, 243), chrome.Color);
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

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
        }

        var app = Application.Current ?? throw new InvalidOperationException("WPF application was not created.");
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Resources/ThemeTokens.xaml", UriKind.Relative),
            });
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(System.IO.Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(System.IO.Path.Combine(directory.FullName, "src", "Scrcap.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate scrcap-windows root.");
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
