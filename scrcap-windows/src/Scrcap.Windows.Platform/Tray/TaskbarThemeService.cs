using Microsoft.Win32;

namespace Scrcap.Windows.Platform.Tray;

public enum TaskbarTheme
{
    Light,
    Dark,
}

public interface ITaskbarThemeService
{
    event EventHandler<TaskbarTheme>? Changed;

    TaskbarTheme Current { get; }
}

public sealed class TaskbarThemeService : ITaskbarThemeService, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private TaskbarTheme lastTheme;

    public TaskbarThemeService()
    {
        lastTheme = Current;
        SystemEvents.UserPreferenceChanged += HandleUserPreferenceChanged;
    }

    public event EventHandler<TaskbarTheme>? Changed;

    public TaskbarTheme Current => UsesLightTaskbar() ? TaskbarTheme.Light : TaskbarTheme.Dark;

    public void RaiseChangedForTest(TaskbarTheme theme) => Changed?.Invoke(this, theme);

    public void Dispose() => SystemEvents.UserPreferenceChanged -= HandleUserPreferenceChanged;

    private void HandleUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color))
        {
            return;
        }

        var current = Current;
        if (current == lastTheme)
        {
            return;
        }

        lastTheme = current;
        Changed?.Invoke(this, current);
    }

    private static bool UsesLightTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
