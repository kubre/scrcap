using Scrcap.Core;
using WpfColor = System.Windows.Media.Color;

namespace Scrcap.Windows.UI.Editor;

public static class EditorRenderMetrics
{
    public const double PixelateBlockDip = 9;
    public const double CounterRadius = 14;
    public const double CropHandleSize = 7;
    public const double ArrowSpreadRadians = 0.46;
    public const double LightCounterTextLumaThreshold = 0.72;

    public static double ArrowHeadLength(double length) =>
        Math.Clamp(length * 0.18, 9, 26);

    public static double RectangleStroke(double baseStroke, double sizeScale, double width, double height) =>
        Math.Min(baseStroke * sizeScale, Math.Max(2, Math.Min(width, height) * 0.08));

    public static double CounterFontSize(int number, double radius) =>
        (Math.Abs(number).ToString(System.Globalization.CultureInfo.InvariantCulture).Length > 2 ? 11 : 14)
        * (radius / CounterRadius);

    public static double TextFontSize(double configuredSize) => configuredSize + 1;

    public static double TextLineHeight(double configuredSize) => TextFontSize(configuredSize) * 1.2;

    public static bool ShouldUseDarkCounterText(WpfColor color) => Luma(color) > LightCounterTextLumaThreshold;

    public static CoreRect ExpansionBounds(Shape shape, double baseStrokeWidth)
    {
        var bounds = shape.Bounds;
        var scale = shape.Size.Scale();
        var pad = shape.Kind switch
        {
            ShapeKind.Rectangle or ShapeKind.Pixelate or ShapeKind.Arrow => Math.Ceiling(Math.Max(baseStrokeWidth * scale, 1) / 2) + 1,
            ShapeKind.Counter => CounterRadius * scale,
            _ => 0,
        };

        return new CoreRect(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);
    }

    public static (int Columns, int Rows) PixelateGrid(double logicalWidth, double logicalHeight)
    {
        var columns = Math.Max(1, (int)Math.Round(logicalWidth / PixelateBlockDip));
        var rows = Math.Max(1, (int)Math.Round(logicalHeight / PixelateBlockDip));
        return (columns, rows);
    }

    private static double Luma(WpfColor color) =>
        ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;
}
