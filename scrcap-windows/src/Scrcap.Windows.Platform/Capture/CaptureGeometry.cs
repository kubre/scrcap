namespace Scrcap.Windows.Platform.Capture;

internal readonly record struct LogicalPoint(double X, double Y);

internal readonly record struct LogicalRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

internal sealed record DpiAwareMonitor(
    string DeviceName,
    LogicalRect LogicalBounds,
    PixelRect PhysicalBounds,
    double DpiScaleX,
    double DpiScaleY);

internal sealed record PhysicalSelection(PixelRect LogicalSourcePixels, PixelRect PhysicalPixels, string DeviceName);

internal static class CaptureGeometry
{
    public static PixelRect LogicalToPhysical(LogicalRect rect, DpiAwareMonitor monitor)
    {
        var clipped = Intersect(rect, monitor.LogicalBounds)
            ?? throw new ArgumentOutOfRangeException(nameof(rect), "Logical rectangle does not intersect the target monitor.");

        return LogicalToPhysicalUnchecked(clipped, monitor);
    }

    public static IReadOnlyList<PhysicalSelection> LogicalToPhysicalSelections(
        LogicalRect selection,
        IReadOnlyList<DpiAwareMonitor> monitors)
    {
        var segments = new List<PhysicalSelection>();
        foreach (var monitor in monitors)
        {
            if (Intersect(selection, monitor.LogicalBounds) is not { } logicalIntersection)
            {
                continue;
            }

            var physical = LogicalToPhysicalUnchecked(logicalIntersection, monitor);
            var logicalPixels = new PixelRect(
                (int)Math.Floor(logicalIntersection.X),
                (int)Math.Floor(logicalIntersection.Y),
                Math.Max(1, (int)Math.Ceiling(logicalIntersection.Right) - (int)Math.Floor(logicalIntersection.X)),
                Math.Max(1, (int)Math.Ceiling(logicalIntersection.Bottom) - (int)Math.Floor(logicalIntersection.Y)));
            segments.Add(new PhysicalSelection(logicalPixels, physical, monitor.DeviceName));
        }

        return segments;
    }

    public static DpiAwareMonitor MonitorUnderPoint(LogicalPoint point, IReadOnlyList<DpiAwareMonitor> monitors)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        foreach (var monitor in monitors)
        {
            if (Contains(monitor.LogicalBounds, point))
            {
                return monitor;
            }
        }

        return monitors
            .OrderBy(monitor => DistanceSquaredToRect(point, monitor.LogicalBounds))
            .ThenBy(monitor => monitor.DeviceName, StringComparer.Ordinal)
            .First();
    }

    private static PixelRect LogicalToPhysicalUnchecked(LogicalRect rect, DpiAwareMonitor monitor)
    {
        var relativeLeft = (rect.X - monitor.LogicalBounds.X) * monitor.DpiScaleX;
        var relativeTop = (rect.Y - monitor.LogicalBounds.Y) * monitor.DpiScaleY;
        var relativeRight = (rect.Right - monitor.LogicalBounds.X) * monitor.DpiScaleX;
        var relativeBottom = (rect.Bottom - monitor.LogicalBounds.Y) * monitor.DpiScaleY;

        var left = monitor.PhysicalBounds.X + (int)Math.Floor(relativeLeft);
        var top = monitor.PhysicalBounds.Y + (int)Math.Floor(relativeTop);
        var right = monitor.PhysicalBounds.X + (int)Math.Ceiling(relativeRight);
        var bottom = monitor.PhysicalBounds.Y + (int)Math.Ceiling(relativeBottom);

        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static LogicalRect? Intersect(LogicalRect left, LogicalRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var edge = Math.Min(left.Right, right.Right);
        var bottom = Math.Min(left.Bottom, right.Bottom);
        return edge > x && bottom > y
            ? new LogicalRect(x, y, edge - x, bottom - y)
            : null;
    }

    private static bool Contains(LogicalRect rect, LogicalPoint point) =>
        point.X >= rect.X && point.X < rect.Right && point.Y >= rect.Y && point.Y < rect.Bottom;

    private static double DistanceSquaredToRect(LogicalPoint point, LogicalRect rect)
    {
        var dx = point.X < rect.X ? rect.X - point.X : point.X >= rect.Right ? point.X - rect.Right : 0;
        var dy = point.Y < rect.Y ? rect.Y - point.Y : point.Y >= rect.Bottom ? point.Y - rect.Bottom : 0;
        return dx * dx + dy * dy;
    }
}
