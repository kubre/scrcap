using System.Windows;
using System.Runtime.InteropServices;
using Scrcap.Windows.Platform.Capture;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Scrcap.Windows.UI.Overlay;

internal sealed record OverlayMonitorBounds(
    int Index,
    PixelRect Bounds,
    bool IsPrimary,
    double DpiScaleX = 1,
    double DpiScaleY = 1)
{
    public Rect LogicalBounds => new(
        Bounds.X / DpiScaleX,
        Bounds.Y / DpiScaleY,
        Bounds.Width / DpiScaleX,
        Bounds.Height / DpiScaleY);
}

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
                screen.Primary,
                DpiScaleFor(screen.Bounds, axis: Axis.X),
                DpiScaleFor(screen.Bounds, axis: Axis.Y)))
            .ToArray();
    }

    public static Rect VirtualOverlayBounds(IEnumerable<OverlayMonitorBounds> monitors)
    {
        var monitorList = monitors.ToArray();
        var logicalBounds = monitorList.Select(monitor => LogicalBoundsFor(monitor, monitorList)).ToArray();
        if (logicalBounds.Length == 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        var left = logicalBounds.Min(bounds => bounds.Left);
        var top = logicalBounds.Min(bounds => bounds.Top);
        var right = logicalBounds.Max(bounds => bounds.Right);
        var bottom = logicalBounds.Max(bounds => bounds.Bottom);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static Rect ToOverlayRect(PixelRect rect, IReadOnlyList<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop)
    {
        var topLeft = PixelPointToLogical(new PixelPoint(rect.X, rect.Y), monitors);
        var bottomRight = PixelPointToLogical(new PixelPoint(rect.Right, rect.Bottom), monitors);
        var left = Math.Min(topLeft.X, bottomRight.X);
        var top = Math.Min(topLeft.Y, bottomRight.Y);
        var right = Math.Max(topLeft.X, bottomRight.X);
        var bottom = Math.Max(topLeft.Y, bottomRight.Y);
        return new Rect(left - overlayLeft, top - overlayTop, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public static int CountIntersectingMonitors(Rect selection, IEnumerable<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop)
    {
        var monitorList = monitors.ToArray();
        return monitorList.Count(monitor => selection.IntersectsWith(ToOverlayRect(monitor, monitorList, overlayLeft, overlayTop)));
    }

    public static Rect ToOverlayRect(OverlayMonitorBounds monitor, IReadOnlyList<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop)
    {
        var bounds = LogicalBoundsFor(monitor, monitors);
        return new Rect(bounds.X - overlayLeft, bounds.Y - overlayTop, bounds.Width, bounds.Height);
    }

    public static Rect LogicalBoundsFor(OverlayMonitorBounds monitor, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return monitor.LogicalBounds;
        }

        var primary = monitors.FirstOrDefault(candidate => candidate.IsPrimary) ?? monitors[0];
        return new Rect(
            LogicalAxisStart(monitor, monitors, primary, Axis.X),
            LogicalAxisStart(monitor, monitors, primary, Axis.Y),
            monitor.Bounds.Width / monitor.DpiScaleX,
            monitor.Bounds.Height / monitor.DpiScaleY);
    }

    public static PixelRect ToCapturePixelRect(Rect selection, IReadOnlyList<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop)
    {
        var monitorList = monitors.ToArray();
        var logicalSelection = new LogicalRect(
            overlayLeft + selection.Left,
            overlayTop + selection.Top,
            selection.Width,
            selection.Height);
        if (monitorList.Length == 0)
        {
            var fallbackX = (int)Math.Floor(logicalSelection.X);
            var fallbackY = (int)Math.Floor(logicalSelection.Y);
            var fallbackRight = (int)Math.Ceiling(logicalSelection.Right);
            var fallbackBottom = (int)Math.Ceiling(logicalSelection.Bottom);
            return new PixelRect(fallbackX, fallbackY, Math.Max(1, fallbackRight - fallbackX), Math.Max(1, fallbackBottom - fallbackY));
        }

        var selections = CaptureGeometry.LogicalToPhysicalSelections(logicalSelection, ToDpiAwareMonitors(monitorList));
        if (selections.Count == 0)
        {
            var centerMonitor = CaptureGeometry.MonitorUnderPoint(
                new LogicalPoint(logicalSelection.X + logicalSelection.Width / 2, logicalSelection.Y + logicalSelection.Height / 2),
                ToDpiAwareMonitors(monitorList));
            return CaptureGeometry.LogicalToPhysical(logicalSelection, centerMonitor);
        }

        var x = selections.Min(selection => selection.PhysicalPixels.X);
        var y = selections.Min(selection => selection.PhysicalPixels.Y);
        var right = selections.Max(selection => selection.PhysicalPixels.Right);
        var bottom = selections.Max(selection => selection.PhysicalPixels.Bottom);
        return new PixelRect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    public static PixelPoint ToCapturePixelPoint(WpfPoint overlayPoint, IReadOnlyList<OverlayMonitorBounds> monitors, double overlayLeft, double overlayTop) =>
        LogicalPointToPixel(new WpfPoint(overlayLeft + overlayPoint.X, overlayTop + overlayPoint.Y), monitors);

    public static WpfPoint CandidateCenter(WindowCandidate candidate) =>
        new(candidate.Bounds.X + candidate.Bounds.Width / 2.0, candidate.Bounds.Y + candidate.Bounds.Height / 2.0);

    public static WpfPoint ClampTagPosition(WpfPoint desired, WpfSize tagSize, WpfSize overlaySize)
    {
        const double margin = 8;
        var x = Math.Min(Math.Max(margin, desired.X), Math.Max(margin, overlaySize.Width - tagSize.Width - margin));
        var y = Math.Min(Math.Max(margin, desired.Y), Math.Max(margin, overlaySize.Height - tagSize.Height - margin));
        return new WpfPoint(x, y);
    }

    private static PixelPoint LogicalPointToPixel(WpfPoint point, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        var monitor = MonitorForLogicalPoint(point, monitors);
        var logical = LogicalBoundsFor(monitor, monitors);
        return new PixelPoint(
            (int)Math.Round(monitor.Bounds.X + (point.X - logical.X) * monitor.DpiScaleX),
            (int)Math.Round(monitor.Bounds.Y + (point.Y - logical.Y) * monitor.DpiScaleY));
    }

    private static DpiAwareMonitor[] ToDpiAwareMonitors(IReadOnlyList<OverlayMonitorBounds> monitors) =>
        monitors
            .Select(monitor =>
            {
                var logical = LogicalBoundsFor(monitor, monitors);
                return new DpiAwareMonitor(
                    monitor.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new LogicalRect(logical.X, logical.Y, logical.Width, logical.Height),
                    monitor.Bounds,
                    monitor.DpiScaleX,
                    monitor.DpiScaleY);
            })
            .ToArray();

    private static WpfPoint PixelPointToLogical(PixelPoint point, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        var monitor = MonitorForPixelPoint(point, monitors);
        var logical = LogicalBoundsFor(monitor, monitors);
        return new WpfPoint(
            logical.X + (point.X - monitor.Bounds.X) / monitor.DpiScaleX,
            logical.Y + (point.Y - monitor.Bounds.Y) / monitor.DpiScaleY);
    }

    private static OverlayMonitorBounds MonitorForLogicalPoint(WpfPoint point, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return new OverlayMonitorBounds(0, new PixelRect(0, 0, 1, 1), true);
        }

        foreach (var monitor in monitors)
        {
            if (LogicalBoundsFor(monitor, monitors).Contains(point))
            {
                return monitor;
            }
        }

        return monitors
            .OrderBy(monitor => DistanceSquared(point, LogicalBoundsFor(monitor, monitors)))
            .First();
    }

    private static double LogicalAxisStart(
        OverlayMonitorBounds target,
        IReadOnlyList<OverlayMonitorBounds> monitors,
        OverlayMonitorBounds primary,
        Axis axis)
    {
        var primaryStart = AxisStart(primary, axis);
        var primaryEnd = AxisEnd(primary, axis);
        var primaryLogicalStart = primaryStart / AxisScale(primary, axis);
        var primaryLogicalEnd = primaryLogicalStart + AxisLength(primary, axis) / AxisScale(primary, axis);
        var targetStart = AxisStart(target, axis);
        var targetEnd = AxisEnd(target, axis);

        if (target.Equals(primary))
        {
            return primaryLogicalStart;
        }

        if (targetStart < primaryEnd && targetEnd > primaryStart)
        {
            return primaryLogicalStart + (targetStart - primaryStart) / AxisScale(primary, axis);
        }

        if (targetStart >= primaryEnd)
        {
            var previous = primary;
            var previousLogicalEnd = primaryLogicalEnd;
            foreach (var monitor in monitors
                         .Where(monitor => !monitor.Equals(primary) && AxisStart(monitor, axis) >= primaryEnd)
                         .OrderBy(monitor => AxisStart(monitor, axis)))
            {
                var gap = Math.Max(0, AxisStart(monitor, axis) - AxisEnd(previous, axis));
                var logicalStart = previousLogicalEnd + gap / AxisScale(monitor, axis);
                if (monitor.Equals(target))
                {
                    return logicalStart;
                }

                previous = monitor;
                previousLogicalEnd = logicalStart + AxisLength(monitor, axis) / AxisScale(monitor, axis);
            }
        }

        if (targetEnd <= primaryStart)
        {
            var next = primary;
            var nextLogicalStart = primaryLogicalStart;
            foreach (var monitor in monitors
                         .Where(monitor => !monitor.Equals(primary) && AxisEnd(monitor, axis) <= primaryStart)
                         .OrderByDescending(monitor => AxisEnd(monitor, axis)))
            {
                var gap = Math.Max(0, AxisStart(next, axis) - AxisEnd(monitor, axis));
                var logicalEnd = nextLogicalStart - gap / AxisScale(monitor, axis);
                var logicalStart = logicalEnd - AxisLength(monitor, axis) / AxisScale(monitor, axis);
                if (monitor.Equals(target))
                {
                    return logicalStart;
                }

                next = monitor;
                nextLogicalStart = logicalStart;
            }
        }

        return targetStart / AxisScale(target, axis);
    }

    private static int AxisStart(OverlayMonitorBounds monitor, Axis axis) =>
        axis == Axis.X ? monitor.Bounds.X : monitor.Bounds.Y;

    private static int AxisEnd(OverlayMonitorBounds monitor, Axis axis) =>
        axis == Axis.X ? monitor.Bounds.Right : monitor.Bounds.Bottom;

    private static int AxisLength(OverlayMonitorBounds monitor, Axis axis) =>
        axis == Axis.X ? monitor.Bounds.Width : monitor.Bounds.Height;

    private static double AxisScale(OverlayMonitorBounds monitor, Axis axis) =>
        axis == Axis.X ? monitor.DpiScaleX : monitor.DpiScaleY;

    private static OverlayMonitorBounds MonitorForPixelPoint(PixelPoint point, IReadOnlyList<OverlayMonitorBounds> monitors)
    {
        if (monitors.Count == 0)
        {
            return new OverlayMonitorBounds(0, new PixelRect(0, 0, 1, 1), true);
        }

        foreach (var monitor in monitors)
        {
            if (Contains(monitor.Bounds, point))
            {
                return monitor;
            }
        }

        return monitors
            .OrderBy(monitor => DistanceSquared(point, monitor.Bounds))
            .First();
    }

    private static double DistanceSquared(WpfPoint point, Rect rect)
    {
        var x = Math.Clamp(point.X, rect.Left, rect.Right);
        var y = Math.Clamp(point.Y, rect.Top, rect.Bottom);
        var dx = point.X - x;
        var dy = point.Y - y;
        return dx * dx + dy * dy;
    }

    private static double DistanceSquared(PixelPoint point, PixelRect rect)
    {
        var x = Math.Clamp(point.X, rect.X, rect.Right);
        var y = Math.Clamp(point.Y, rect.Y, rect.Bottom);
        var dx = point.X - x;
        var dy = point.Y - y;
        return dx * dx + dy * dy;
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.X
        && point.Y >= rect.Y
        && point.X <= rect.Right
        && point.Y <= rect.Bottom;

    private static double DpiScaleFor(System.Drawing.Rectangle bounds, Axis axis)
    {
        var rect = new NativeRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var monitor = MonitorFromRect(ref rect, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return 1;
        }

        try
        {
            return GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out var dpiY) == 0
                ? Math.Max(0.1, (axis == Axis.X ? dpiX : dpiY) / 96.0)
                : 1;
        }
        catch (DllNotFoundException)
        {
            return 1;
        }
        catch (EntryPointNotFoundException)
        {
            return 1;
        }
    }

    private enum Axis
    {
        X,
        Y,
    }

    private enum MonitorDpiType
    {
        Effective = 0,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public NativeRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
}
