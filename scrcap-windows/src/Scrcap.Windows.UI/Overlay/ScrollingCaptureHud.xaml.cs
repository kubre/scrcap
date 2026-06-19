using System.Windows;
using Scrcap.Windows.Platform.Capture;
using System.Windows.Input;
using System.Windows.Threading;

namespace Scrcap.Windows.UI.Overlay;

public partial class ScrollingCaptureHud : Window
{
    private readonly Action cancel;

    public ScrollingCaptureHud(Action cancel)
    {
        this.cancel = cancel;
        InitializeComponent();
    }

    public static ScrollingCaptureHud ShowFor(PixelRect region, Action cancel)
    {
        var hud = new ScrollingCaptureHud(cancel);
        var position = PlacementFor(region, OverlayGeometry.CreateMonitorBounds(), hud.Width, hud.Height);
        hud.Left = position.X;
        hud.Top = position.Y;
        hud.Show();
        hud.Activate();
        return hud;
    }

    internal static System.Windows.Point PlacementFor(PixelRect region, IReadOnlyList<OverlayMonitorBounds> monitors, double hudWidth, double hudHeight)
    {
        var monitor = MonitorFor(region, monitors);
        var bounds = OverlayGeometry.LogicalBoundsFor(monitor, monitors);
        const double margin = 18;
        return new System.Windows.Point(
            Math.Max(bounds.Left + margin, bounds.Right - hudWidth - margin),
            Math.Min(bounds.Bottom - hudHeight - margin, bounds.Top + margin));
    }

    public void UpdateProgress(ScrollingCaptureProgress progress)
    {
        var frameWord = progress.FrameCount == 1 ? "frame" : "frames";
        var stop = progress.StopReason is null ? string.Empty : $" - {StopText(progress.StopReason.Value)}";
        ProgressText.Text = $"{progress.FrameCount} {frameWord} - {progress.OutputHeight}/{progress.MaxPixelHeight}px{stop}";
    }

    public async Task HideForCaptureAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.InvokeAsync(Hide, DispatcherPriority.Send, cancellationToken).Task.ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken).Task.ConfigureAwait(false);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, cancellationToken).Task.ConfigureAwait(false);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
    }

    public Task ShowAfterCaptureAsync(CancellationToken cancellationToken) =>
        Dispatcher.InvokeAsync(
            () =>
            {
                if (!IsVisible)
                {
                    Show();
                }
            },
            DispatcherPriority.Send).Task;

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        cancel();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            cancel();
            e.Handled = true;
        }
    }

    private static string StopText(ScrollingCaptureStopReason reason) =>
        reason switch
        {
            ScrollingCaptureStopReason.BottomReached => "bottom",
            ScrollingCaptureStopReason.Cancelled => "cancelled",
            ScrollingCaptureStopReason.Timeout => "timeout",
            ScrollingCaptureStopReason.MaxHeightReached => "max height",
            ScrollingCaptureStopReason.MemoryCapReached => "memory cap",
            ScrollingCaptureStopReason.AlignmentFailed => "alignment",
            ScrollingCaptureStopReason.CaptureFailed => "failed",
            _ => "done",
        };

    private static OverlayMonitorBounds MonitorFor(PixelRect region, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return new OverlayMonitorBounds(0, region, true);
        }

        var center = new PixelPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
        return monitors
            .OrderBy(monitor => DistanceSquared(center, monitor.Bounds))
            .First();
    }

    private static double DistanceSquared(PixelPoint point, PixelRect rect)
    {
        var x = Math.Clamp(point.X, rect.X, rect.Right);
        var y = Math.Clamp(point.Y, rect.Y, rect.Bottom);
        var dx = point.X - x;
        var dy = point.Y - y;
        return dx * dx + dy * dy;
    }
}
