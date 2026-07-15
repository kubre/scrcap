using System.Drawing;
using System.Windows.Forms;
using Scrcap.Core;

namespace Scrcap.Windows.Platform.Tray;

public sealed record TrayCommand(string Title, AppAction? Action);

public interface ITrayService : IDisposable
{
    event EventHandler<AppAction>? CaptureRequested;

    event EventHandler? PreferencesRequested;

    event EventHandler? QuitRequested;

    string CurrentIconKey { get; }

    void Show();

    void Notify(string title, string message);
}

public static class TrayServiceExtensions
{
    /// <summary>
    /// Emits the capture-copied notification only when the user's live settings allow it.
    /// All clipboard-success paths should use this contract instead of calling Notify directly.
    /// </summary>
    public static void NotifyCopy(this ITrayService tray, Settings settings, string message = "Capture copied to the clipboard.")
    {
        if (!settings.SuppressCopyNotification)
        {
            tray.Notify("Copied to clipboard", message);
        }
    }
}

public sealed class NotifyIconTrayService : ITrayService
{
    private readonly NotifyIcon notifyIcon;
    private readonly ITaskbarThemeService themeService;
    private readonly Func<Keymap> keymapProvider;
    private readonly Dictionary<AppAction, ToolStripMenuItem> captureItems = [];

    public NotifyIconTrayService(ITaskbarThemeService themeService, Func<Keymap>? keymapProvider = null)
    {
        this.themeService = themeService;
        this.keymapProvider = keymapProvider ?? (() => Keymap.Defaults);
        CurrentIconKey = IconKeyFor(themeService.Current);
        notifyIcon = new NotifyIcon
        {
            Text = "scrcap",
            Visible = false,
            ContextMenuStrip = BuildMenu(),
        };
        ApplyIcon(themeService.Current);
        themeService.Changed += HandleThemeChanged;
    }

    public event EventHandler<AppAction>? CaptureRequested;

    public event EventHandler? PreferencesRequested;

    public event EventHandler? QuitRequested;

    public string CurrentIconKey { get; private set; }

    public void Show() => notifyIcon.Visible = true;

    public void Notify(string title, string message)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        themeService.Changed -= HandleThemeChanged;
        if (themeService is IDisposable disposableThemeService)
        {
            disposableThemeService.Dispose();
        }

        notifyIcon.Dispose();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => RefreshShortcutLabels();
        AddCapture(menu, "Capture Region", AppAction.CaptureRegion);
        AddCapture(menu, "Capture Window", AppAction.CaptureWindow);
        AddCapture(menu, "Capture Fullscreen", AppAction.CaptureFullscreen);
        AddCapture(menu, "Scrolling Capture", AppAction.CaptureScrolling);
        AddCapture(menu, "Delayed Region Capture", AppAction.CaptureDelayed);
        AddCapture(menu, "Repeat Last Capture", AppAction.RepeatLast);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Preferences", null, (_, _) => PreferencesRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Quit", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void AddCapture(ContextMenuStrip menu, string title, AppAction action)
    {
        var item = new ToolStripMenuItem(title, null, (_, _) => CaptureRequested?.Invoke(this, action))
        {
            ShortcutKeyDisplayString = ShortcutDisplayFor(keymapProvider(), action),
        };
        captureItems[action] = item;
        menu.Items.Add(item);
    }

    private void RefreshShortcutLabels()
    {
        var keymap = keymapProvider();
        foreach (var (action, item) in captureItems)
        {
            item.ShortcutKeyDisplayString = ShortcutDisplayFor(keymap, action);
        }
    }

    public static string ShortcutDisplayFor(Keymap keymap, AppAction action) =>
        keymap.ChordFor(action)?.WindowsDisplayValue ?? string.Empty;

    private void HandleThemeChanged(object? sender, TaskbarTheme theme) =>
        ApplyIcon(theme);

    private void ApplyIcon(TaskbarTheme theme)
    {
        CurrentIconKey = IconKeyFor(theme);
        var previous = notifyIcon.Icon;
        notifyIcon.Icon = LoadIcon(theme);
        previous?.Dispose();
    }

    private static string IconKeyFor(TaskbarTheme theme) =>
        theme == TaskbarTheme.Light ? "scrcap-tray-taskbar-light.ico" : "scrcap-tray-taskbar-dark.ico";

    private static Icon LoadIcon(TaskbarTheme theme)
    {
        var iconKey = IconKeyFor(theme);
        var resourceName = $"Scrcap.Windows.Platform.Tray.Assets.{iconKey}";
        using var stream = typeof(NotifyIconTrayService).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Tray icon resource is missing: {resourceName}");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
