using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Scrcap.Core;
using Scrcap.Windows.UI.Resources;
using WinForms = System.Windows.Forms;

namespace Scrcap.Windows.UI.Preferences;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow(SettingsStore store)
    {
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, store.Settings.ThemeMode);
        }

        InitializeComponent();
        DataContext = new PreferencesViewModel(store);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel && viewModel.Save())
        {
            if (System.Windows.Application.Current is { } app)
            {
                AppThemeService.Apply(app.Resources, viewModel.SelectedThemeMode);
            }

            Close();
            return;
        }

        System.Windows.MessageBox.Show("Could not save preferences.", "scrcap", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is PreferencesViewModel viewModel)
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

    private void DragSurface_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
        {
            DragMove();
        }
    }

    private void ShortcutTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not PreferencesViewModel viewModel
            || sender is not System.Windows.Controls.TextBox { Tag: string actionName }
            || !Enum.TryParse<AppAction>(actionName, out var action))
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        viewModel.RecordShortcut(action, key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
