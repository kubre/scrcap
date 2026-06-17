using Scrcap.Core;

namespace Scrcap.Windows.Platform.Hotkeys;

public sealed record RegisteredHotkey(AppAction Action, KeyChord Chord);

public sealed record FailedHotkey(AppAction Action, KeyChord Chord, string Message);

public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<AppAction>? Pressed;

    IReadOnlyList<RegisteredHotkey> Registered { get; }

    IReadOnlyList<FailedHotkey> Failed { get; }

    void Register(Keymap keymap);

    void UnregisterAll();

    IDisposable Suspend();
}
