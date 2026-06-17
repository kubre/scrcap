using Scrcap.Core;
using Scrcap.Windows.Platform.Hotkeys;

namespace Scrcap.Windows.Platform.Tests;

public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void TestHookRaisesActionWithoutRegisteringSystemHotkey()
    {
        using var service = new GlobalHotkeyService();
        AppAction? received = null;
        service.Pressed += (_, action) => received = action;

        service.RaiseForTest(AppAction.CaptureRegion);

        Assert.Equal(AppAction.CaptureRegion, received);
    }
}
