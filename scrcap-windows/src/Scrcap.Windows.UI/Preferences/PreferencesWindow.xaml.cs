using System.Windows;
using System.Windows.Input;
using Scrcap.Core;
using Scrcap.Windows.Platform.Startup;
using Scrcap.Windows.UI.Resources;
using WinForms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace Scrcap.Windows.UI.Preferences;

public partial class PreferencesWindow : Window
{
    private readonly PreferencesViewModel viewModel;
    private AppAction? recordingAction;

    public PreferencesWindow(
        SettingsStore store,
        int selectedTabIndex = 0,
        ILaunchAtLoginService? launchAtLoginService = null)
    {
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, store.Settings.ThemeMode);
        }

        InitializeComponent();
        viewModel = new PreferencesViewModel(store, launchAtLoginService);
        viewModel.ThemeModeChanged += ViewModel_ThemeModeChanged;
        DataContext = viewModel;
        Closing += (_, _) => viewModel.FlushPendingWrites();
        Closed += (_, _) => viewModel.ThemeModeChanged -= ViewModel_ThemeModeChanged;
        Loaded += (_, _) =>
        {
            if (selectedTabIndex >= 0 && selectedTabIndex < PreferencesTabs.Items.Count)
            {
                PreferencesTabs.SelectedIndex = selectedTabIndex;
            }
        };
    }

    public event EventHandler<bool>? ShortcutRecordingChanged;

    private static void ViewModel_ThemeModeChanged(object? sender, ThemeMode theme)
    {
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, theme);
        }
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ResetAll();
    }

    private void ResetPalette_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ResetPalette();
    }

    private void PickSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose scrcap save folder",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(viewModel.SaveFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : viewModel.SaveFolder,
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            viewModel.SaveFolder = dialog.SelectedPath;
            viewModel.FlushPendingWrites();
        }
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string propertyName })
        {
            return;
        }

        var currentHex = propertyName switch
        {
            nameof(PreferencesViewModel.WindowBackgroundHex) => viewModel.WindowBackgroundHex,
            nameof(PreferencesViewModel.CanvasExtensionBackgroundHex) => viewModel.CanvasExtensionBackgroundHex,
            nameof(PreferencesViewModel.Palette1) => viewModel.Palette1,
            nameof(PreferencesViewModel.Palette2) => viewModel.Palette2,
            nameof(PreferencesViewModel.Palette3) => viewModel.Palette3,
            nameof(PreferencesViewModel.Palette4) => viewModel.Palette4,
            nameof(PreferencesViewModel.Palette5) => viewModel.Palette5,
            _ => "#FFFFFF",
        };

        using var dialog = new WinForms.ColorDialog
        {
            FullOpen = true,
            Color = ToDrawingColor(currentHex),
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return;
        }

        var selected = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        switch (propertyName)
        {
            case nameof(PreferencesViewModel.WindowBackgroundHex):
                viewModel.WindowBackgroundHex = selected;
                break;
            case nameof(PreferencesViewModel.CanvasExtensionBackgroundHex):
                viewModel.CanvasExtensionBackgroundHex = selected;
                break;
            case nameof(PreferencesViewModel.Palette1):
                viewModel.Palette1 = selected;
                break;
            case nameof(PreferencesViewModel.Palette2):
                viewModel.Palette2 = selected;
                break;
            case nameof(PreferencesViewModel.Palette3):
                viewModel.Palette3 = selected;
                break;
            case nameof(PreferencesViewModel.Palette4):
                viewModel.Palette4 = selected;
                break;
            case nameof(PreferencesViewModel.Palette5):
                viewModel.Palette5 = selected;
                break;
        }

        viewModel.FlushPendingWrites();
    }

    private void ShortcutRecorder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string actionName } button
            && Enum.TryParse<AppAction>(actionName, out var action))
        {
            recordingAction = action;
            viewModel.BeginShortcutRecording(action);
            ShortcutRecordingChanged?.Invoke(this, true);
            button.Focus();
        }
    }

    private void ShortcutRecorder_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not WpfButton { Tag: string actionName }
            || !Enum.TryParse<AppAction>(actionName, out var action))
        {
            return;
        }

        if (recordingAction != action)
        {
            if (e.Key is Key.Space or Key.Enter or Key.Return)
            {
                recordingAction = action;
                viewModel.BeginShortcutRecording(action);
                ShortcutRecordingChanged?.Invoke(this, true);
                e.Handled = true;
            }

            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        viewModel.RecordShortcut(action, key, Keyboard.Modifiers);
        recordingAction = null;
        ShortcutRecordingChanged?.Invoke(this, false);
        e.Handled = true;
    }

    private static System.Drawing.Color ToDrawingColor(string hex)
    {
        if (WpfColorConverter.ConvertFromString(Settings.NormalizeHexColor(hex) ?? "#FFFFFF") is not WpfColor color)
        {
            return System.Drawing.Color.White;
        }

        return System.Drawing.Color.FromArgb(color.R, color.G, color.B);
    }
}
