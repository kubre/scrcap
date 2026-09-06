using Scrcap.Core;
using Scrcap.Windows.Platform.Hotkeys;

namespace Scrcap.Windows.Platform.Tests;

public sealed class GlobalHotkeyServiceTests
{
    [Fact]
    public void NestedSuspensionRestoresLatestMappingOnlyAfterLastOwnerCloses()
    {
        using var service = new GlobalHotkeyService();
        service.Register(new Keymap(new Dictionary<AppAction, KeyChord>()));
        var first = service.Suspend();
        var second = service.Suspend();
        service.Register(new Keymap(new Dictionary<AppAction, KeyChord>
        {
            [AppAction.CaptureRegion] = new("unsupported-test-key", ChordModifiers.None),
        }));
        Assert.Empty(service.Failed);
        first.Dispose();
        Assert.Empty(service.Failed);
        second.Dispose();
        Assert.Single(service.Failed); // registration was deferred, not silently lost
        Assert.Equal("unsupported-test-key", service.Failed[0].Chord.Key);
    }

    [Fact]
    public void SuspensionCanBeReleasedAfterServiceShutdown()
    {
        var service = new GlobalHotkeyService();
        service.Register(new Keymap(new Dictionary<AppAction, KeyChord>()));
        var suspension = service.Suspend();
        service.Dispose();
        suspension.Dispose();
        service.Dispose();
        Assert.Empty(service.Registered);
    }

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
