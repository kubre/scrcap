using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class KeymapEngineTests
{
    [Theory]
    [InlineData("Alt+Shift+2", "opt+shift+2", "Alt+Shift+2")]
    [InlineData("ctrl+alt+space", "ctrl+opt+space", "Ctrl+Alt+SPACE")]
    [InlineData("win+shift+r", "shift+cmd+r", "Shift+Win+R")]
    public void ChordParserAcceptsWindowsAndPortableModifierNames(string input, string storage, string display)
    {
        Assert.True(KeyChord.TryParse(input, out var chord));
        Assert.Equal(storage, chord.StringValue);
        Assert.Equal(display, chord.WindowsDisplayValue);
    }

    [Fact]
    public void RebindingUsedChordStealsVictim()
    {
        var keymap = new Keymap(Keymap.Defaults.Bindings);
        var chord = Keymap.Defaults.ChordFor(AppAction.CaptureRegion)!.Value;

        var victim = keymap.Set(chord, AppAction.CaptureWindow);

        Assert.Equal(AppAction.CaptureRegion, victim);
        Assert.Null(keymap.ChordFor(AppAction.CaptureRegion));
        Assert.Equal(chord, keymap.ChordFor(AppAction.CaptureWindow));
    }

    [Theory]
    [InlineData("Alt+F4")]
    [InlineData("Win+L")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Win+D")]
    [InlineData("Win+Tab")]
    public void WindowsReservedGlobalShortcutsAreRejected(string input)
    {
        Assert.True(KeyChord.TryParse(input, out var chord));

        Assert.False(Keymap.IsUsableGlobalShortcut(chord));
    }

    [Fact]
    public void BareUnmodifiedKeysAreRejectedForGlobalShortcuts()
    {
        Assert.True(KeyChord.TryParse("r", out var chord));

        Assert.False(Keymap.IsUsableGlobalShortcut(chord));
    }

    [Fact]
    public void ModifiedNonReservedKeyIsUsable()
    {
        Assert.True(KeyChord.TryParse("Alt+Shift+R", out var chord));

        Assert.True(Keymap.IsUsableGlobalShortcut(chord));
    }
}
