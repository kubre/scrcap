using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace Scrcap.Windows.UI.Editor;

public sealed class EditorCanvas : FrameworkElement
{
    private WpfPoint? dragStart;
    private WpfPoint? dragCurrent;
    private Rect imageRect;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(EditorViewModel),
            typeof(EditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SourceBitmapProperty =
        DependencyProperty.Register(
            nameof(SourceBitmap),
            typeof(BitmapSource),
            typeof(EditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public EditorViewModel? ViewModel
    {
        get => (EditorViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public BitmapSource? SourceBitmap
    {
        get => (BitmapSource?)GetValue(SourceBitmapProperty);
        set => SetValue(SourceBitmapProperty, value);
    }

    public event EventHandler<CoreRect>? CropCommitted;

    public event EventHandler? DocumentChanged;

    public CorePoint WindowPointToImagePoint(WpfPoint point)
    {
        if (ViewModel?.Document is null || imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return new CorePoint(0, 0);
        }

        return new CorePoint(
            Math.Clamp((point.X - imageRect.X) / ViewModel.Zoom, 0, ViewModel.Document.Size.Width),
            Math.Clamp((point.Y - imageRect.Y) / ViewModel.Zoom, 0, ViewModel.Document.Size.Height));
    }

    public byte[] FlattenPng(int scale)
    {
        if (ViewModel?.Document is not { } document || SourceBitmap is null)
        {
            return [];
        }

        scale = scale == 1 ? 1 : 2;
        var width = Math.Max(1, (int)Math.Round(document.Size.Width * scale));
        var height = Math.Max(1, (int)Math.Round(document.Size.Height * scale));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            DrawDocument(context, document.Size, zoom: 1, includeBackground: true);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        dragStart = e.GetPosition(this);
        dragCurrent = dragStart;
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (dragStart is not null)
        {
            dragCurrent = e.GetPosition(this);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (dragStart is not { } start || ViewModel is null)
        {
            return;
        }

        var end = e.GetPosition(this);
        ReleaseMouseCapture();
        dragStart = null;
        dragCurrent = null;

        var startImage = WindowPointToImagePoint(start);
        var endImage = WindowPointToImagePoint(end);
        var rect = CoreRect.FromPoints(startImage, endImage);

        if (ViewModel.ActiveTool == EditorTool.Crop)
        {
            if (rect.Width >= 2 && rect.Height >= 2)
            {
                CropCommitted?.Invoke(this, rect);
            }
        }
        else if (ShouldCommit(ViewModel.ActiveTool, rect, startImage, endImage))
        {
            ViewModel.CommitShape(startImage, endImage);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle((WpfBrush)FindResource("BrushCanvas"), null, new Rect(RenderSize));

        if (ViewModel?.Document is not { } document || SourceBitmap is null)
        {
            return;
        }

        imageRect = ImageRect(document.Size, RenderSize, ViewModel.Zoom);
        drawingContext.PushTransform(new TranslateTransform(imageRect.X, imageRect.Y));
        drawingContext.PushTransform(new ScaleTransform(ViewModel.Zoom, ViewModel.Zoom));
        DrawDocument(drawingContext, document.Size, zoom: 1, includeBackground: true);

        foreach (var shape in document.Shapes)
        {
            DrawShape(drawingContext, shape, ViewModel, zoom: 1);
        }

        if (dragStart is { } start && dragCurrent is { } current)
        {
            DrawPreview(drawingContext, ViewModel, WindowPointToImagePoint(start), WindowPointToImagePoint(current));
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private void DrawDocument(DrawingContext context, CoreSize size, double zoom, bool includeBackground)
    {
        var documentRect = new Rect(0, 0, size.Width, size.Height);
        if (includeBackground)
        {
            context.DrawRectangle(WpfBrushes.White, null, documentRect);
        }

        if (SourceBitmap is not null)
        {
            context.DrawImage(SourceBitmap, documentRect);
        }
    }

    private void DrawPreview(DrawingContext context, EditorViewModel viewModel, CorePoint start, CorePoint end)
    {
        if (viewModel.ActiveTool == EditorTool.Text)
        {
            return;
        }

        var preview = new Shape(
            viewModel.ActiveTool == EditorTool.Counter
                ? new ShapeKind.Counter(viewModel.Document?.NextCounterNumber ?? 1)
                : viewModel.ActiveTool == EditorTool.Pixelate
                    ? new ShapeKind.Pixelate()
                    : viewModel.ActiveTool == EditorTool.Crop
                        ? new ShapeKind.Rectangle()
                        : viewModel.ActiveTool == EditorTool.Rectangle
                            ? new ShapeKind.Rectangle()
                            : new ShapeKind.Arrow(),
            viewModel.ColorIndex,
            viewModel.ActiveSize,
            start,
            end);
        DrawShape(context, preview, viewModel, zoom: 1, isPreview: true);
    }

    private void DrawShape(DrawingContext context, Shape shape, EditorViewModel viewModel, double zoom, bool isPreview = false)
    {
        var color = ColorForShape(shape, viewModel);
        var brush = new SolidColorBrush(color);
        var pen = new WpfPen(brush, Math.Max(1, viewModel.Settings.StrokeWidth * shape.Size.Scale() / zoom))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        if (isPreview)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        var start = ToPoint(shape.Start);
        var end = ToPoint(shape.End);

        switch (shape.Kind)
        {
            case ShapeKind.Arrow:
                DrawArrow(context, pen, start, end, shape.Size.Scale());
                break;
            case ShapeKind.Rectangle:
                context.DrawRoundedRectangle(null, pen, Normalize(start, end), 5, 5);
                break;
            case ShapeKind.Pixelate:
                DrawPixelate(context, Normalize(start, end));
                context.DrawRoundedRectangle(null, pen, Normalize(start, end), 4, 4);
                break;
            case ShapeKind.Counter counter:
                DrawCounter(context, brush, color, end, counter.Number, shape.Size.Scale());
                break;
            case ShapeKind.Text text:
                DrawText(context, brush, start, text.Value, text.Size);
                break;
        }
    }

    private static void DrawArrow(DrawingContext context, WpfPen pen, WpfPoint start, WpfPoint end, double scale)
    {
        context.DrawLine(pen, start, end);
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2)
        {
            return;
        }

        var unitX = dx / length;
        var unitY = dy / length;
        var headLength = Math.Clamp(length * 0.25, 10 * scale, 28 * scale);
        const double spread = Math.PI / 7;
        DrawArrowHeadLine(context, pen, end, unitX, unitY, headLength, spread);
        DrawArrowHeadLine(context, pen, end, unitX, unitY, headLength, -spread);
    }

    private static void DrawArrowHeadLine(DrawingContext context, WpfPen pen, WpfPoint end, double unitX, double unitY, double length, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var x = unitX * cos - unitY * sin;
        var y = unitX * sin + unitY * cos;
        context.DrawLine(pen, end, new WpfPoint(end.X - x * length, end.Y - y * length));
    }

    private void DrawPixelate(DrawingContext context, Rect rect)
    {
        if (SourceBitmap is null || rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        var clipped = Rect.Intersect(rect, new Rect(0, 0, SourceBitmap.PixelWidth, SourceBitmap.PixelHeight));
        if (clipped.IsEmpty)
        {
            return;
        }

        context.PushClip(new RectangleGeometry(clipped));
        for (var y = clipped.Top; y < clipped.Bottom; y += 9)
        {
            for (var x = clipped.Left; x < clipped.Right; x += 9)
            {
                var sampleX = Math.Clamp((int)Math.Round(x), 0, SourceBitmap.PixelWidth - 1);
                var sampleY = Math.Clamp((int)Math.Round(y), 0, SourceBitmap.PixelHeight - 1);
                var pixels = new byte[4];
                SourceBitmap.CopyPixels(new Int32Rect(sampleX, sampleY, 1, 1), pixels, 4, 0);
                var color = WpfColor.FromArgb(255, pixels[2], pixels[1], pixels[0]);
                context.DrawRectangle(new SolidColorBrush(color), null, new Rect(x, y, Math.Min(9, clipped.Right - x), Math.Min(9, clipped.Bottom - y)));
            }
        }

        context.Pop();
    }

    private void DrawCounter(DrawingContext context, WpfBrush brush, WpfColor color, WpfPoint center, int number, double scale)
    {
        var radius = 13 * scale;
        context.DrawEllipse(brush, null, center, radius, radius);
        var textColor = Luma(color) > 0.5 ? WpfBrushes.Black : WpfBrushes.White;
        var fontSize = number >= 100 ? 10 * scale : 13 * scale;
        var text = new FormattedText(
            number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            fontSize,
            textColor,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new WpfPoint(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private void DrawText(DrawingContext context, WpfBrush brush, WpfPoint start, string value, double size)
    {
        var formatted = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, start);
    }

    private WpfColor ColorForShape(Shape shape, EditorViewModel viewModel)
    {
        var palette = viewModel.Settings.PaletteHex;
        var index = Math.Clamp(shape.ColorIndex, 0, palette.Count - 1);
        return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(palette[index]);
    }

    private static bool ShouldCommit(EditorTool tool, CoreRect rect, CorePoint start, CorePoint end) =>
        tool switch
        {
            EditorTool.Counter => true,
            EditorTool.Text => true,
            EditorTool.Pixelate => rect.Width > 1 && rect.Height > 1,
            EditorTool.Arrow => Distance(start, end) >= 2,
            _ => rect.Width >= 2 && rect.Height >= 2,
        };

    private static double Distance(CorePoint start, CorePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static Rect ImageRect(CoreSize documentSize, WpfSize available, double zoom)
    {
        var width = documentSize.Width * zoom;
        var height = documentSize.Height * zoom;
        return new Rect(Math.Max(0, (available.Width - width) / 2), Math.Max(0, (available.Height - height) / 2), width, height);
    }

    private static WpfPoint ToPoint(CorePoint point) => new(point.X, point.Y);

    private static Rect Normalize(WpfPoint a, WpfPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double Luma(WpfColor color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
}
