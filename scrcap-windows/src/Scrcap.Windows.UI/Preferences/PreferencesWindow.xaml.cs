using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scrcap.Core;
using Scrcap.Windows.Platform.Startup;
using Scrcap.Windows.Platform.Updates;
using Scrcap.Windows.UI.Resources;
using DrawingColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace Scrcap.Windows.UI.Preferences;

public partial class PreferencesWindow : Window
{
    private readonly SettingsStore store;

    public PreferencesWindow(
        SettingsStore store,
        ILaunchAtLoginService? launchAtLoginService = null,
        IUpdateChecker? updateChecker = null)
    {
        this.store = store;
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, store.Settings.ThemeMode);
        }

        InitializeComponent();
        ChromeWindow.Attach(this);
        DataContext = new PreferencesViewModel(store, launchAtLoginService, updateChecker);
        store.SettingsChanged += Store_SettingsChanged;
        Closed += (_, _) => store.SettingsChanged -= Store_SettingsChanged;
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel
            && System.Windows.MessageBox.Show(
                "Restore every preference to its default value?",
                "Reset scrcap preferences",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) == MessageBoxResult.OK)
        {
            viewModel.ResetAll();
        }
    }

    private void PickSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel viewModel)
        {
            return;
        }

        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "Choose scrcap save folder",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(viewModel.SaveFolder) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : viewModel.SaveFolder,
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            viewModel.SaveFolder = dialog.SelectedPath;
            viewModel.OnPropertyChanged(nameof(viewModel.SaveFolder));
        }
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PreferencesViewModel viewModel
            || sender is not FrameworkElement { Tag: string propertyName })
        {
            return;
        }

        using var dialog = new WinForms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = DrawingColorFromHex(CurrentColorHex(viewModel, propertyName)),
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            viewModel.SetColor(propertyName, $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        }
    }

    private void DragSurface_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }

    private void ShortcutTextBox_GotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (TryShortcutAction(sender, out var viewModel, out var action))
        {
            viewModel.BeginShortcutRecording(action);
        }
    }

    private void ShortcutTextBox_LostKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (TryShortcutAction(sender, out var viewModel, out var action))
        {
            viewModel.EndShortcutRecording(action);
        }
    }

    private void ShortcutTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!TryShortcutAction(sender, out var viewModel, out var action))
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        viewModel.RecordShortcut(action, key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel)
        {
            await viewModel.CheckForUpdatesAsync();
        }
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel)
        {
            OpenWebAddress(viewModel.SourceLink);
        }
    }

    private void OpenLatestRelease_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel { LatestReleaseUrl: { } url })
        {
            OpenWebAddress(url.AbsoluteUri);
        }
    }

    private void Store_SettingsChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, store.Settings.ThemeMode);
        }
    }

    private static void OpenWebAddress(string address)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(address)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "Could not open link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string CurrentColorHex(PreferencesViewModel viewModel, string propertyName) =>
        propertyName switch
        {
            nameof(viewModel.WindowBackgroundHex) => viewModel.WindowBackgroundHex,
            nameof(viewModel.Palette1) => viewModel.Palette1,
            nameof(viewModel.Palette2) => viewModel.Palette2,
            nameof(viewModel.Palette3) => viewModel.Palette3,
            nameof(viewModel.Palette4) => viewModel.Palette4,
            nameof(viewModel.Palette5) => viewModel.Palette5,
            nameof(viewModel.CanvasExtensionBackgroundHex) => viewModel.CanvasExtensionBackgroundHex,
            _ => "#FFFFFF",
        };

    private static DrawingColor DrawingColorFromHex(string hex)
    {
        var normalized = Settings.NormalizeHexColor(hex) ?? "#FFFFFF";
        return DrawingColor.FromArgb(
            Convert.ToInt32(normalized.Substring(1, 2), 16),
            Convert.ToInt32(normalized.Substring(3, 2), 16),
            Convert.ToInt32(normalized.Substring(5, 2), 16));
    }

    private bool TryShortcutAction(object sender, out PreferencesViewModel viewModel, out AppAction action)
    {
        viewModel = null!;
        action = default;
        if (DataContext is not PreferencesViewModel candidateViewModel
            || sender is not System.Windows.Controls.TextBox { Tag: string actionName }
            || !Enum.TryParse(actionName, out action))
        {
            return false;
        }

        viewModel = candidateViewModel;
        return true;
    }
}
