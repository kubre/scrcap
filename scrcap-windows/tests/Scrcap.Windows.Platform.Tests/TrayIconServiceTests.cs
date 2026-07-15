using Scrcap.Core;
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

    [Fact]
    public void TrayIconAssetsAreEmbedded()
    {
        var resources = typeof(NotifyIconTrayService).Assembly.GetManifestResourceNames();

        Assert.Contains("Scrcap.Windows.Platform.Tray.Assets.scrcap-tray-taskbar-light.ico", resources);
        Assert.Contains("Scrcap.Windows.Platform.Tray.Assets.scrcap-tray-taskbar-dark.ico", resources);
    }

    [Fact]
    public void ShortcutDisplayUsesWindowsHotkeyNames()
    {
        var keymap = new Keymap(new Dictionary<AppAction, KeyChord>
        {
            [AppAction.CaptureRegion] = new("1", ChordModifiers.Option | ChordModifiers.Shift),
            [AppAction.RepeatLast] = new("r", ChordModifiers.Control | ChordModifiers.Command),
        });

        Assert.Equal("Alt+Shift+1", NotifyIconTrayService.ShortcutDisplayFor(keymap, AppAction.CaptureRegion));
        Assert.Equal("Ctrl+Win+R", NotifyIconTrayService.ShortcutDisplayFor(keymap, AppAction.RepeatLast));
        Assert.Empty(NotifyIconTrayService.ShortcutDisplayFor(keymap, AppAction.CaptureWindow));
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
