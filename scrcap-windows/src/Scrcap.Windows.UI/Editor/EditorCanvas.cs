using System.IO;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using Scrcap.Core.Diagnostics;
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
    private bool isDragOut;

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

    public double SourcePixelScaleX { get; set; } = 1;

    public double SourcePixelScaleY { get; set; } = 1;

    public event EventHandler<CoreRect>? CropCommitted;

    public event EventHandler<CorePoint>? TextRequested;

    public event EventHandler<AutoExpandResult>? AutoExpanded;

    public event EventHandler? DragOutRequested;

    public event EventHandler? DocumentChanged;

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new FrameworkElementAutomationPeer(this);

    public CorePoint WindowPointToImagePoint(WpfPoint point) =>
        WindowPointToImagePoint(point, allowOutsideImage: ViewModel?.Settings.AutoExpandCanvas == true);

    public WpfPoint ImagePointToWindowPoint(CorePoint point)
    {
        EnsureImageRect();
        return new WpfPoint(imageRect.X + point.X * (ViewModel?.Zoom ?? 1), imageRect.Y + point.Y * (ViewModel?.Zoom ?? 1));
    }

    private CorePoint WindowPointToImagePoint(WpfPoint point, bool allowOutsideImage)
    {
        EnsureImageRect();
        if (ViewModel?.Document is null || imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return new CorePoint(0, 0);
        }

        var x = (point.X - imageRect.X) / ViewModel.Zoom;
        var y = (point.Y - imageRect.Y) / ViewModel.Zoom;
        return allowOutsideImage
            ? new CorePoint(x, y)
            : new CorePoint(Math.Clamp(x, 0, ViewModel.Document.Size.Width), Math.Clamp(y, 0, ViewModel.Document.Size.Height));
    }

    public byte[] FlattenPng(int scale)
    {
        var viewModel = ViewModel;
        if (viewModel?.Document is not { } document || SourceBitmap is null)
        {
            return [];
        }

        scale = scale == 1 ? 1 : 2;
        var targetScaleX = SourcePixelScaleX * scale;
        var targetScaleY = SourcePixelScaleY * scale;
        var width = Math.Max(1, (int)Math.Round(document.Size.Width * targetScaleX));
        var height = Math.Max(1, (int)Math.Round(document.Size.Height * targetScaleY));
        using var span = ScrcapDiagnostics.Start(
            "flatten_to_png",
            ("scale", scale),
            ("pixelWidth", width),
            ("pixelHeight", height),
            ("shapeCount", document.Shapes.Count));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(targetScaleX, targetScaleY));
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

    internal byte[] RenderCropPreviewPngForTests(CoreRect rect)
    {
        if (ViewModel?.Document is not { } document || SourceBitmap is null)
        {
            return [];
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            DrawDocument(context, document.Size, zoom: 1, includeBackground: true);
            DrawCropPreview(context, rect, document.Size);
        }

        var width = Math.Max(1, (int)Math.Round(document.Size.Width));
        var height = Math.Max(1, (int)Math.Round(document.Size.Height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
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
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CaptureMouse();
            dragStart = e.GetPosition(this);
            dragCurrent = null;
            isDragOut = true;
            e.Handled = true;
            return;
        }

        if (ViewModel?.ActiveTool == EditorTool.Text)
        {
            TextRequested?.Invoke(this, WindowPointToImagePoint(e.GetPosition(this), allowOutsideImage: false));
            e.Handled = true;
            return;
        }

        CaptureMouse();
        dragStart = e.GetPosition(this);
        dragCurrent = dragStart;
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (dragStart is not null && IsMouseCaptured)
        {
            if (isDragOut)
            {
                var current = e.GetPosition(this);
                if (Distance(dragStart.Value, current) >= SystemParameters.MinimumHorizontalDragDistance)
                {
                    dragStart = null;
                    dragCurrent = null;
                    isDragOut = false;
                    ReleaseMouseCapture();
                    DragOutRequested?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

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
        if (isDragOut)
        {
            isDragOut = false;
            InvalidateVisual();
            return;
        }

        var allowOutside = ViewModel.Settings.AutoExpandCanvas && ViewModel.ActiveTool != EditorTool.Crop;
        var startImage = WindowPointToImagePoint(start, allowOutside);
        var endImage = WindowPointToImagePoint(end, allowOutside);
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
            var growth = ViewModel.CommitShape(startImage, endImage);
            if (growth.HasGrowth)
            {
                AutoExpanded?.Invoke(this, growth);
            }

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
            var allowOutside = ViewModel.Settings.AutoExpandCanvas && ViewModel.ActiveTool != EditorTool.Crop;
            DrawPreview(drawingContext, ViewModel, WindowPointToImagePoint(start, allowOutside), WindowPointToImagePoint(current, allowOutside));
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

        if (viewModel.ActiveTool == EditorTool.Crop)
        {
            DrawCropPreview(context, CoreRect.FromPoints(start, end), viewModel.Document?.Size ?? new CoreSize(0, 0));
            return;
        }

        var preview = new Shape(
            viewModel.ActiveTool == EditorTool.Counter
                ? new ShapeKind.Counter(viewModel.Document?.NextCounterNumber ?? 1)
                : viewModel.ActiveTool == EditorTool.Pixelate
                    ? new ShapeKind.Pixelate()
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
                var rect = Normalize(start, end);
                var rectanglePen = pen.Clone();
                rectanglePen.Thickness = EditorRenderMetrics.RectangleStroke(viewModel.Settings.StrokeWidth, shape.Size.Scale(), rect.Width, rect.Height);
                rectanglePen.Freeze();
                context.DrawRoundedRectangle(null, rectanglePen, rect, 3, 3);
                break;
            case ShapeKind.Pixelate:
                DrawPixelate(context, Normalize(start, end), exportScale);
                break;
            case ShapeKind.Counter counter:
                DrawCounter(context, brush, color, end, counter.Number, shape.Size.Scale());
                break;
            case ShapeKind.Text text:
                DrawText(context, brush, start, text.Value, text.Size, text.MaxWidth);
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
        var headLength = EditorRenderMetrics.ArrowHeadLength(length);
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

        var allowOutside = ViewModel.Settings.AutoExpandCanvas && ViewModel.ActiveTool != EditorTool.Crop;
        var startImage = WindowPointToImagePoint(start, allowOutside);
        var endImage = WindowPointToImagePoint(end, allowOutside);
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

        var warmRedrawStopwatch = ScrcapDiagnostics.IsEnabled ? Stopwatch.StartNew() : null;
        var logicalSourceBounds = new Rect(
            0,
            0,
            SourceBitmap.PixelWidth / Math.Max(0.001, SourcePixelScaleX),
            SourceBitmap.PixelHeight / Math.Max(0.001, SourcePixelScaleY));
        var clipped = Rect.Intersect(rect, logicalSourceBounds);
        if (clipped.IsEmpty)
        {
            return;
        }

        var sourceRect = new Rect(
            clipped.X * SourcePixelScaleX,
            clipped.Y * SourcePixelScaleY,
            clipped.Width * SourcePixelScaleX,
            clipped.Height * SourcePixelScaleY);
        if (!PixelateRenderer.TryGetPixelBounds(sourceRect, SourceBitmap, out var bounds))
        {
            return;
        }

        var grid = EditorRenderMetrics.PixelateGrid(bounds.Width / SourcePixelScaleX, bounds.Height / SourcePixelScaleY);
        var bitmap = pixelateRenderer.Render(SourceBitmap, bounds, grid.Columns, grid.Rows, exportScale, out var cacheHit);
        context.DrawImage(
            bitmap,
            new Rect(
                bounds.X / SourcePixelScaleX,
                bounds.Y / SourcePixelScaleY,
                bounds.Width / SourcePixelScaleX,
                bounds.Height / SourcePixelScaleY));
        if (cacheHit && warmRedrawStopwatch is not null)
        {
            warmRedrawStopwatch.Stop();
            ScrcapDiagnostics.Measure(
                "warm_pixelate_redraw",
                warmRedrawStopwatch.Elapsed,
                ("width", bounds.Width),
                ("height", bounds.Height),
                ("exportScale", exportScale));
        }
    }

    private void DrawCounter(DrawingContext context, WpfBrush brush, WpfColor color, WpfPoint center, int number, double scale)
    {
        var radius = EditorRenderMetrics.CounterRadius * scale;
        context.DrawEllipse(brush, null, center, radius, radius);
        var textColor = EditorRenderMetrics.ShouldUseDarkCounterText(color) ? WpfBrushes.Black : WpfBrushes.White;
        var fontSize = EditorRenderMetrics.CounterFontSize(number, radius);
        var text = new FormattedText(
            number.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            fontSize,
            textColor,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(text, new WpfPoint(center.X - text.Width / 2, center.Y - text.Height / 2));
    }

    private void DrawText(DrawingContext context, WpfBrush brush, WpfPoint start, string value, double size, double? maxWidth)
    {
        var formatted = new FormattedText(
            value,
            CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            EditorRenderMetrics.TextFontSize(size),
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        formatted.LineHeight = EditorRenderMetrics.TextLineHeight(size);
        if (maxWidth is > 0)
        {
            formatted.MaxTextWidth = maxWidth.Value;
        }

        context.DrawText(formatted, start);
    }

    private void DrawCropPreview(DrawingContext context, CoreRect rect, CoreSize documentSize)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || documentSize.Width <= 0 || documentSize.Height <= 0)
        {
            return;
        }

        var documentRect = new Rect(0, 0, documentSize.Width, documentSize.Height);
        var crop = Rect.Intersect(
            new Rect(rect.X, rect.Y, rect.Width, rect.Height),
            documentRect);
        if (crop.IsEmpty)
        {
            return;
        }

        var dim = new SolidColorBrush(WpfColor.FromArgb(88, 0, 0, 0));
        dim.Freeze();
        var overlay = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new RectangleGeometry(documentRect),
            new RectangleGeometry(crop));
        context.DrawGeometry(dim, null, overlay);
        DrawHazardSelection(context, crop);
    }

    private void DrawHazardSelection(DrawingContext context, Rect rect)
    {
        var blackAnts = new WpfPen(WpfBrushes.Black, 3)
        {
            DashStyle = new DashStyle([4, 4], 0),
        };
        var whiteAnts = new WpfPen(WpfBrushes.White, 1)
        {
            DashStyle = new DashStyle([4, 4], 0),
        };
        var redAnts = new WpfPen((WpfBrush)FindResource("BrushAccent"), 2)
        {
            DashStyle = new DashStyle([8, 6], 0),
        };
        blackAnts.Freeze();
        whiteAnts.Freeze();
        redAnts.Freeze();
        context.DrawRectangle(null, blackAnts, rect);
        context.DrawRectangle(null, whiteAnts, rect);
        context.DrawRectangle(null, redAnts, rect);

        const double size = EditorRenderMetrics.CropHandleSize;
        var handlePen = new WpfPen(WpfBrushes.Black, 1);
        handlePen.Freeze();
        foreach (var point in new[]
                 {
                     rect.TopLeft,
                     rect.TopRight,
                     rect.BottomLeft,
                     rect.BottomRight,
                 })
        {
            var handle = new Rect(point.X - size / 2, point.Y - size / 2, size, size);
            context.DrawRectangle(WpfBrushes.White, handlePen, handle);
        }
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

    private static double Distance(WpfPoint start, WpfPoint end)
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
