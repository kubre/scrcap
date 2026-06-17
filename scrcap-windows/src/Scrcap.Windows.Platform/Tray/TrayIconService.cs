using System.Drawing;
using System.Runtime.InteropServices;
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
        notifyIcon.Icon = CreateIcon(theme);
        previous?.Dispose();
    }

    private static string IconKeyFor(TaskbarTheme theme) =>
        theme == TaskbarTheme.Light ? "scrcap-tray-taskbar-light.ico" : "scrcap-tray-taskbar-dark.ico";

    private static Icon CreateIcon(TaskbarTheme theme)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var background = new SolidBrush(theme == TaskbarTheme.Light ? Color.FromArgb(25, 25, 28) : Color.White))
        using (var accent = new SolidBrush(Color.FromArgb(227, 14, 32)))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRoundedRectangle(background, new Rectangle(4, 4, 24, 24), 5);
            graphics.FillRectangle(accent, new Rectangle(10, 10, 12, 3));
            graphics.FillRectangle(accent, new Rectangle(10, 10, 3, 12));
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
