using System.Windows;
using Scrcap.Windows.Platform.Capture;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Scrcap.Windows.UI.Overlay;

internal sealed record OverlayMonitorBounds(int Index, PixelRect Bounds, bool IsPrimary);

internal static class OverlayGeometry
{
    public static IReadOnlyList<OverlayMonitorBounds> CreateMonitorBounds()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        return screens
            .Select((screen, index) => new OverlayMonitorBounds(
                index + 1,
                new PixelRect(
                    screen.Bounds.X,
                    screen.Bounds.Y,
                    screen.Bounds.Width,
                    screen.Bounds.Height),
                screen.Primary))
            .ToArray();
    }

    public static Rect ToOverlayRect(PixelRect rect, double overlayLeft, double overlayTop) =>
        new(rect.X - overlayLeft, rect.Y - overlayTop, rect.Width, rect.Height);

    public static int CountIntersectingMonitors(Rect selection, IEnumerable<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop) =>
        monitors.Count(monitor => selection.IntersectsWith(ToOverlayRect(monitor.Bounds, overlayLeft, overlayTop)));

    public static WpfPoint CandidateCenter(WindowCandidate candidate) =>
        new(candidate.Bounds.X + candidate.Bounds.Width / 2.0, candidate.Bounds.Y + candidate.Bounds.Height / 2.0);

    public static WpfPoint ClampTagPosition(WpfPoint desired, WpfSize tagSize, WpfSize overlaySize)
    {
        const double margin = 8;
        var x = Math.Min(Math.Max(margin, desired.X), Math.Max(margin, overlaySize.Width - tagSize.Width - margin));
        var y = Math.Min(Math.Max(margin, desired.Y), Math.Max(margin, overlaySize.Height - tagSize.Height - margin));
        return new WpfPoint(x, y);
    }
}
