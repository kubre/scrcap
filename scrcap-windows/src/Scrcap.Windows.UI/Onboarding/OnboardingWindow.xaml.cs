using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.Windows.UI.Onboarding;

public partial class OnboardingWindow : Window
{
    private readonly Action openPreferences;

    public OnboardingWindow(string regionShortcut, Action openPreferences)
    {
        InitializeComponent();
        ChromeWindow.Attach(this);
        RegionShortcut = regionShortcut;
        this.openPreferences = openPreferences;
        DataContext = this;
    }

    public string RegionShortcut { get; }

    private void Preferences_Click(object sender, RoutedEventArgs e)
    {
        Close();
        openPreferences();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowBackground_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !IsInsideButton(e.OriginalSource as DependencyObject))
        {
            DragMove();
        }
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
