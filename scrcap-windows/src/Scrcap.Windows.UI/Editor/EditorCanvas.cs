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
    private readonly PixelateRenderer pixelateRenderer = new();
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
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceBitmapChanged));

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
        EnsureImageRect();
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
        var viewModel = ViewModel;
        if (viewModel?.Document is not { } document || SourceBitmap is null)
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
            foreach (var shape in document.Shapes)
            {
                DrawShape(context, shape, viewModel, zoom: 1, exportScale: scale);
            }

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
        if (e.ChangedButton != MouseButton.Left || !IsImageHit(e.GetPosition(this)))
        {
            return;
        }

        Focus();
        CaptureMouse();
        dragStart = e.GetPosition(this);
        dragCurrent = dragStart;
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (dragStart is not null && IsMouseCaptured)
        {
            dragCurrent = AdjustWindowEndPoint(dragStart.Value, e.GetPosition(this), Keyboard.Modifiers);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || dragStart is not { } start || ViewModel is null)
        {
            return;
        }

        ReleaseMouseCapture();
        var end = AdjustWindowEndPoint(start, e.GetPosition(this), Keyboard.Modifiers);
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
        DrawShape(context, preview, viewModel, zoom: 1);
    }

    private void DrawShape(DrawingContext context, Shape shape, EditorViewModel viewModel, double zoom, double exportScale = 1)
    {
        var color = ColorForShape(shape, viewModel);
        var brush = new SolidColorBrush(color);
        var pen = new WpfPen(brush, Math.Max(1, viewModel.Settings.StrokeWidth * shape.Size.Scale() / zoom))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

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
                DrawPixelate(context, Normalize(start, end), exportScale);
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
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 2)
        {
            return;
        }

        var unitX = dx / length;
        var unitY = dy / length;
        var headLength = Math.Clamp(length * 0.18, 9 * scale, 26 * scale);
        var shaftEnd = new WpfPoint(end.X - unitX * headLength * 0.6, end.Y - unitY * headLength * 0.6);
        context.DrawLine(pen, start, shaftEnd);

        var angle = Math.Atan2(unitY, unitX);
        const double spread = 0.46;
        var figure = new PathFigure { StartPoint = end, IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new WpfPoint(
            end.X - headLength * Math.Cos(angle - spread),
            end.Y - headLength * Math.Sin(angle - spread)), true));
        figure.Segments.Add(new LineSegment(new WpfPoint(
            end.X - headLength * Math.Cos(angle + spread),
            end.Y - headLength * Math.Sin(angle + spread)), true));
        context.DrawGeometry(pen.Brush, null, new PathGeometry([figure]));
    }

    private WpfPoint AdjustWindowEndPoint(WpfPoint start, WpfPoint end, ModifierKeys modifiers)
    {
        if (ViewModel is null || !modifiers.HasFlag(ModifierKeys.Shift))
        {
            return end;
        }

        var startImage = WindowPointToImagePoint(start);
        var endImage = WindowPointToImagePoint(end);
        endImage = ViewModel.ActiveTool switch
        {
            EditorTool.Rectangle => ConstrainToSquare(startImage, endImage),
            EditorTool.Arrow => SnapTo45Degrees(startImage, endImage),
            _ => endImage,
        };
        return ImagePointToWindowPoint(endImage);
    }

    private void DrawPixelate(DrawingContext context, Rect rect, double exportScale)
    {
        if (SourceBitmap is null || rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        var clipped = Rect.Intersect(rect, new Rect(0, 0, SourceBitmap.PixelWidth, SourceBitmap.PixelHeight));
        if (clipped.IsEmpty || !PixelateRenderer.TryGetPixelBounds(clipped, SourceBitmap, out var bounds))
        {
            return;
        }

        var bitmap = pixelateRenderer.Render(SourceBitmap, bounds, blockSize: 9, exportScale);
        context.DrawImage(bitmap, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
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

    private bool IsImageHit(WpfPoint point)
    {
        EnsureImageRect();
        return ViewModel?.Document is not null && SourceBitmap is not null && imageRect.Contains(point);
    }

    private void EnsureImageRect()
    {
        if (ViewModel?.Document is not { } document)
        {
            return;
        }

        var width = RenderSize.Width > 0 ? RenderSize.Width : ActualWidth > 0 ? ActualWidth : Width;
        var height = RenderSize.Height > 0 ? RenderSize.Height : ActualHeight > 0 ? ActualHeight : Height;
        if (width > 0 && height > 0)
        {
            imageRect = ImageRect(document.Size, new WpfSize(width, height), ViewModel.Zoom);
        }
    }

    private WpfPoint ImagePointToWindowPoint(CorePoint point) =>
        new(imageRect.X + point.X * (ViewModel?.Zoom ?? 1), imageRect.Y + point.Y * (ViewModel?.Zoom ?? 1));

    private static CorePoint ConstrainToSquare(CorePoint start, CorePoint end)
    {
        var side = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
        return new CorePoint(
            start.X + side * (end.X >= start.X ? 1 : -1),
            start.Y + side * (end.Y >= start.Y ? 1 : -1));
    }

    private static CorePoint SnapTo45Degrees(CorePoint start, CorePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001)
        {
            return end;
        }

        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        return new CorePoint(
            start.X + Math.Cos(snapped) * length,
            start.Y + Math.Sin(snapped) * length);
    }

    private static WpfPoint ToPoint(CorePoint point) => new(point.X, point.Y);

    private static Rect Normalize(WpfPoint a, WpfPoint b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static double Luma(WpfColor color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;

    private static void SourceBitmapChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is EditorCanvas canvas)
        {
            canvas.pixelateRenderer.Clear();
        }
    }
}
