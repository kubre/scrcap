using Scrcap.Windows.Platform.Tray;

namespace Scrcap.Windows.Platform.Tests;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void IconKeyFollowsTaskbarThemeChanges()
    {
        var theme = new FakeThemeService(TaskbarTheme.Light);
        using var tray = new NotifyIconTrayService(theme);

        Assert.Equal("scrcap-tray-taskbar-light.ico", tray.CurrentIconKey);

        theme.Set(TaskbarTheme.Dark);

        Assert.Equal("scrcap-tray-taskbar-dark.ico", tray.CurrentIconKey);
    }

    private sealed class FakeThemeService(TaskbarTheme current) : ITaskbarThemeService
    {
        public event EventHandler<TaskbarTheme>? Changed;

        public TaskbarTheme Current { get; private set; } = current;

        public void Set(TaskbarTheme theme)
        {
            Current = theme;
            Changed?.Invoke(this, theme);
        }
    }
}
