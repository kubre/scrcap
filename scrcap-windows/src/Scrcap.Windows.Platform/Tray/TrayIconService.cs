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

public sealed class NotifyIconTrayService : ITrayService
{
    private readonly NotifyIcon notifyIcon;
    private readonly ITaskbarThemeService themeService;

    public NotifyIconTrayService(ITaskbarThemeService themeService)
    {
        this.themeService = themeService;
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

    private void AddCapture(ContextMenuStrip menu, string title, AppAction action) =>
        menu.Items.Add(title, null, (_, _) => CaptureRequested?.Invoke(this, action));

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
