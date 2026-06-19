using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Scrcap.Windows.UI.Chrome;

public sealed class ScrcapWindowChrome : ContentControl, INotifyPropertyChanged
{
    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(nameof(TitleText), typeof(string), typeof(ScrcapWindowChrome), new PropertyMetadata("SCRCAP"));

    public static readonly DependencyProperty SubtitleTextProperty =
        DependencyProperty.Register(nameof(SubtitleText), typeof(string), typeof(ScrcapWindowChrome), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderActionsProperty =
        DependencyProperty.Register(nameof(HeaderActions), typeof(object), typeof(ScrcapWindowChrome), new PropertyMetadata(null));

    public static readonly DependencyProperty FrameCornerRadiusProperty =
        DependencyProperty.Register(nameof(FrameCornerRadius), typeof(CornerRadius), typeof(ScrcapWindowChrome), new PropertyMetadata(new CornerRadius()));

    static ScrcapWindowChrome()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ScrcapWindowChrome), new FrameworkPropertyMetadata(typeof(ScrcapWindowChrome)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string SubtitleText
    {
        get => (string)GetValue(SubtitleTextProperty);
        set => SetValue(SubtitleTextProperty, value);
    }

    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public CornerRadius FrameCornerRadius
    {
        get => (CornerRadius)GetValue(FrameCornerRadiusProperty);
        set => SetValue(FrameCornerRadiusProperty, value);
    }

    public string MaximizeGlyph => Window.GetWindow(this)?.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        HookButton("PART_MinimizeButton", Minimize);
        HookButton("PART_MaximizeButton", MaximizeRestore);
        HookButton("PART_CloseButton", CloseWindow);

        if (Window.GetWindow(this) is { } window)
        {
            window.StateChanged += (_, _) => OnPropertyChanged(nameof(MaximizeGlyph));
        }
    }

    private void HookButton(string name, RoutedEventHandler handler)
    {
        if (GetTemplateChild(name) is System.Windows.Controls.Button button)
        {
            button.Click += handler;
        }
    }

    private void Minimize(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.MinimizeWindow(window);
        }
    }

    private void MaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(window);
        }
        else
        {
            SystemCommands.MaximizeWindow(window);
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
        {
            SystemCommands.CloseWindow(window);
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
