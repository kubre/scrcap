using Scrcap.Core;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Scrcap.Windows.Platform.Hotkeys;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly List<RegisteredHotkey> registered = [];
    private readonly List<FailedHotkey> failed = [];
    private readonly Dictionary<int, AppAction> idToAction = [];
    private readonly HotkeyWindow window;
    private Keymap? lastKeymap;
    private int nextId = 1;

    public GlobalHotkeyService()
    {
        window = new HotkeyWindow(
            id => idToAction.TryGetValue(id, out var action) ? action : null,
            action => Pressed?.Invoke(this, action));
    }

    public event EventHandler<AppAction>? Pressed;

    public IReadOnlyList<RegisteredHotkey> Registered => registered;

    public IReadOnlyList<FailedHotkey> Failed => failed;

    public void Register(Keymap keymap)
    {
        UnregisterAll();
        failed.Clear();
        lastKeymap = new Keymap(keymap.Bindings);
        foreach (var (action, chord) in keymap.Bindings)
        {
            var id = nextId++;
            if (!TryVirtualKey(chord.Key, out var virtualKey))
            {
                failed.Add(new FailedHotkey(action, chord, $"Unsupported hotkey key '{chord.Key}'."));
                continue;
            }

            if (!RegisterHotKey(window.Handle, id, Modifiers(chord.Modifiers), virtualKey))
            {
                failed.Add(new FailedHotkey(action, chord, new Win32Exception(Marshal.GetLastWin32Error()).Message));
                continue;
            }

            idToAction[id] = action;
            registered.Add(new RegisteredHotkey(action, chord));
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in idToAction.Keys.ToArray())
        {
            UnregisterHotKey(window.Handle, id);
        }

        idToAction.Clear();
        registered.Clear();
    }

    public IDisposable Suspend()
    {
        var restore = lastKeymap;
        UnregisterAll();
        return new Scope(() =>
        {
            if (restore is not null)
            {
                Register(restore);
            }
        });
    }

    public void Dispose()
    {
        UnregisterAll();
        window.DestroyHandle();
    }

    internal void RaiseForTest(AppAction action)
    {
        Pressed?.Invoke(this, action);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            dispose();
        }
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        private readonly Func<int, AppAction?> resolve;
        private readonly Action<AppAction> onHotkey;

        public HotkeyWindow(Func<int, AppAction?> resolve, Action<AppAction> onHotkey)
        {
            this.resolve = resolve;
            this.onHotkey = onHotkey;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey && resolve(m.WParam.ToInt32()) is { } action)
            {
                onHotkey(action);
                return;
            }

            base.WndProc(ref m);
        }
    }

    private static uint Modifiers(ChordModifiers modifiers)
    {
        var value = ModNoRepeat;
        if (modifiers.HasFlag(ChordModifiers.Option))
        {
            value |= ModAlt;
        }

        if (modifiers.HasFlag(ChordModifiers.Control))
        {
            value |= ModControl;
        }

        if (modifiers.HasFlag(ChordModifiers.Shift))
        {
            value |= ModShift;
        }

        if (modifiers.HasFlag(ChordModifiers.Command))
        {
            value |= ModWin;
        }

        return value;
    }

    private static bool TryVirtualKey(string key, out uint virtualKey)
    {
        virtualKey = key switch
        {
            "space" => 0x20,
            "esc" or "escape" => 0x1B,
            _ when key.Length == 1 && key[0] is >= 'a' and <= 'z' => char.ToUpperInvariant(key[0]),
            _ when key.Length == 1 && key[0] is >= '0' and <= '9' => key[0],
            _ when key.StartsWith('f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 => (uint)(0x70 + f - 1),
            _ => 0,
        };

        return virtualKey != 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
