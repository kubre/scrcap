using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scrcap.Core;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Scrcap.Windows.UI.Editor;

public sealed class EditorCanvas : System.Windows.Controls.Canvas
{
    private static readonly DoubleCollection RegionPreviewDashPattern = [4, 4];
    private readonly PixelateRenderer pixelateRenderer = new();
    private readonly DispatcherTimer regionAntTimer;
    private WpfTextBox? textEditor;
    private CorePoint? textEditorAnchor;
    private EditorViewModel? subscribedViewModel;
    private CorePoint? dragStart;
    private CorePoint? dragCurrent;
    private WpfPoint? panStart;
    private WpfPoint panStartOffset;
    private bool isPanning;
    private bool isDragOut;
    private WpfPoint? dragOutStart;
    private Rect imageRect;
    private double manipulationZoomAccumulator = 1;
    private double regionAntPhase;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(EditorViewModel),
            typeof(EditorCanvas),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ViewModelChanged));

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

    public EditorCanvas()
    {
        IsManipulationEnabled = true;
        regionAntTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Render, (_, _) =>
        {
            regionAntPhase = (regionAntPhase + 1) % 16;
            InvalidateVisual();
        }, Dispatcher);
        regionAntTimer.Stop();
        Unloaded += (_, _) => StopRegionPreviewAnimation();
    }

    public event EventHandler<CoreRect>? CropCommitted;

    public event EventHandler? DocumentChanged;

    public event EventHandler<CanvasExpansionRequestedEventArgs>? CanvasExpansionRequested;

    public event EventHandler? DragOutRequested;

    public bool IsTextEditing => textEditor is not null;

    public double SourcePixelsPerDipX { get; private set; } = 1;

    public double SourcePixelsPerDipY { get; private set; } = 1;

    public void SetSourcePixelsPerDip(double x, double y)
    {
        SourcePixelsPerDipX = NormalizeSourceScale(x);
        SourcePixelsPerDipY = NormalizeSourceScale(y);
        pixelateRenderer.Clear();
        InvalidateVisual();
    }

    public CorePoint WindowPointToImagePoint(WpfPoint point, bool clamp = true)
    {
        EnsureImageRect();
        if (ViewModel?.Document is null || imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return new CorePoint(0, 0);
        }

        var imagePoint = new CorePoint(
            (point.X - imageRect.X) / ViewModel.Zoom,
            (point.Y - imageRect.Y) / ViewModel.Zoom);
        return clamp
            ? new CorePoint(
                Math.Clamp(imagePoint.X, 0, ViewModel.Document.Size.Width),
                Math.Clamp(imagePoint.Y, 0, ViewModel.Document.Size.Height))
            : imagePoint;
    }

    public byte[] FlattenPng(int scale)
    {
        var viewModel = ViewModel;
        if (viewModel?.Document is not { } document || SourceBitmap is null)
        {
            return [];
        }

        var requestedScale = scale == 1 ? 1 : 2;
        var exportScaleX = ResolveExportScale(requestedScale, SourcePixelsPerDipX);
        var exportScaleY = ResolveExportScale(requestedScale, SourcePixelsPerDipY);
        var width = Math.Max(1, (int)Math.Round(document.Size.Width * exportScaleX));
        var height = Math.Max(1, (int)Math.Round(document.Size.Height * exportScaleY));
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(exportScaleX, exportScaleY));
            DrawDocument(context, document.Size, zoom: 1, includeBackground: true);
            var clipsAnnotations = RequiresDocumentClip(document, viewModel);
            if (clipsAnnotations)
            {
                context.PushClip(DocumentClip(document.Size));
            }

            foreach (var shape in document.Shapes)
            {
                DrawShape(context, shape, viewModel, zoom: 1, exportScale: Math.Max(exportScaleX, exportScaleY));
            }

            if (clipsAnnotations)
            {
                context.Pop();
            }

            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96 * exportScaleX, 96 * exportScaleY, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && IsPanGestureActive)
        {
            Focus();
            CommitTextEditing();
            BeginPan(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || !IsImageHit(e.GetPosition(this)))
        {
            return;
        }

        Focus();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            CommitTextEditing();
            CaptureMouse();
            isDragOut = true;
            dragOutStart = e.GetPosition(this);
            e.Handled = true;
            return;
        }

        if (ViewModel?.ActiveTool == EditorTool.Text)
        {
            var wasEditing = textEditor is not null;
            CommitTextEditing();
            if (!wasEditing)
            {
                BeginTextEditing(e.GetPosition(this));
            }

            e.Handled = true;
            return;
        }

        CommitTextEditing();
        CaptureMouse();
        dragStart = WindowPointToImagePoint(e.GetPosition(this), clamp: false);
        dragCurrent = dragStart;
        StartRegionPreviewAnimation();
        InvalidateVisual();
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        if (isDragOut && dragOutStart is { } start && IsMouseCaptured)
        {
            var current = e.GetPosition(this);
            if ((current - start).Length > 6)
            {
                isDragOut = false;
                dragOutStart = null;
                ReleaseMouseCapture();
                DragOutRequested?.Invoke(this, EventArgs.Empty);
            }

            e.Handled = true;
            return;
        }

        if (isPanning && panStart is { } panStartPoint && IsMouseCaptured && ViewModel is not null)
        {
            var current = e.GetPosition(this);
            ViewModel.SetPan(
                panStartOffset.X + current.X - panStartPoint.X,
                panStartOffset.Y + current.Y - panStartPoint.Y);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (dragStart is not null && IsMouseCaptured)
        {
            var dragStartImage = dragStart.Value;
            var current = AdjustImageEndPoint(dragStartImage, WindowPointToImagePoint(e.GetPosition(this), clamp: false), Keyboard.Modifiers);
            if (ViewModel?.ActiveTool != EditorTool.Crop)
            {
                ExpandCanvasForDrag(ref dragStartImage, ref current);
                dragStart = dragStartImage;
            }

            dragCurrent = current;
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && isDragOut)
        {
            isDragOut = false;
            dragOutStart = null;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && isPanning)
        {
            EndPan();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left || dragStart is not { } start || ViewModel is null)
        {
            return;
        }

        ReleaseMouseCapture();
        var end = AdjustImageEndPoint(start, WindowPointToImagePoint(e.GetPosition(this), clamp: false), Keyboard.Modifiers);
        if (ViewModel.ActiveTool != EditorTool.Crop)
        {
            ExpandCanvasForDrag(ref start, ref end);
        }

        dragStart = null;
        dragCurrent = null;
        StopRegionPreviewAnimation();

        var startImage = ViewModel.ActiveTool == EditorTool.Crop ? ClampToDocument(start) : start;
        var endImage = ViewModel.ActiveTool == EditorTool.Crop ? ClampToDocument(end) : end;
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

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);

        if (e.Delta == 0 || ViewModel is null || IsTextEditing)
        {
            return;
        }

        Focus();
        var position = e.GetPosition(this);
        if (IsZoomWheelGesture)
        {
            var direction = e.Delta > 0 ? +1 : -1;
            ViewModel.ZoomAt(direction, position.X, position.Y, ActualWidth, ActualHeight);
        }
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            ViewModel.PanBy(0, e.Delta);
        }
        else
        {
            return;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnManipulationStarting(ManipulationStartingEventArgs e)
    {
        base.OnManipulationStarting(e);

        if (ViewModel is null || IsTextEditing)
        {
            return;
        }

        Focus();
        manipulationZoomAccumulator = 1;
        e.ManipulationContainer = this;
        e.Mode = ManipulationModes.Translate | ManipulationModes.Scale;
        e.Handled = true;
    }

    protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
    {
        base.OnManipulationDelta(e);

        if (ViewModel is null || IsTextEditing)
        {
            return;
        }

        var translation = e.DeltaManipulation.Translation;
        if (Math.Abs(translation.X) >= 0.01 || Math.Abs(translation.Y) >= 0.01)
        {
            ViewModel.PanBy(translation.X, translation.Y);
        }

        var scale = (e.DeltaManipulation.Scale.X + e.DeltaManipulation.Scale.Y) / 2;
        if (scale > 0 && Math.Abs(scale - 1) >= 0.001)
        {
            manipulationZoomAccumulator *= scale;
            var origin = e.ManipulationOrigin;
            if (manipulationZoomAccumulator >= 1.08)
            {
                ViewModel.ZoomAt(+1, origin.X, origin.Y, ActualWidth, ActualHeight);
                manipulationZoomAccumulator = 1;
            }
            else if (manipulationZoomAccumulator <= 0.92)
            {
                ViewModel.ZoomAt(-1, origin.X, origin.Y, ActualWidth, ActualHeight);
                manipulationZoomAccumulator = 1;
            }
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnManipulationCompleted(ManipulationCompletedEventArgs e)
    {
        base.OnManipulationCompleted(e);
        manipulationZoomAccumulator = 1;
    }

    protected override void OnPreviewKeyDown(WpfKeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.Space && !IsTextEditing)
        {
            UpdateToolCursor();
        }
    }

    protected override void OnPreviewKeyUp(WpfKeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        if (e.Key == Key.Space)
        {
            if (isPanning)
            {
                EndPan();
            }

            UpdateToolCursor();
        }
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (isPanning)
        {
            EndPan();
        }

        UpdateToolCursor();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle((WpfBrush)FindResource("BrushCanvas"), null, new Rect(RenderSize));

        if (ViewModel?.Document is not { } document || SourceBitmap is null)
        {
            return;
        }

        imageRect = ImageRect(document.Size, RenderSize);
        drawingContext.PushTransform(new TranslateTransform(imageRect.X, imageRect.Y));
        drawingContext.PushTransform(new ScaleTransform(ViewModel.Zoom, ViewModel.Zoom));
        DrawDocument(drawingContext, document.Size, zoom: 1, includeBackground: true);
        var clipsAnnotations = RequiresDocumentClip(document, ViewModel);
        if (clipsAnnotations)
        {
            drawingContext.PushClip(DocumentClip(document.Size));
        }

        foreach (var shape in document.Shapes)
        {
            DrawShape(drawingContext, shape, ViewModel, zoom: 1);
        }

        if (dragStart is { } start && dragCurrent is { } current)
        {
            DrawPreview(drawingContext, ViewModel, start, current);
        }

        if (clipsAnnotations)
        {
            drawingContext.Pop();
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static RectangleGeometry DocumentClip(CoreSize size) =>
        new(new Rect(0, 0, size.Width, size.Height));

    private static bool RequiresDocumentClip(AnnotationDocument document, EditorViewModel viewModel) =>
        document.Shapes.Any(shape => !Contains(document.Size, AnnotationBounds(shape, viewModel)));

    private static bool Contains(CoreSize size, CoreRect bounds) =>
        bounds.MinX >= 0
        && bounds.MinY >= 0
        && bounds.MaxX <= size.Width
        && bounds.MaxY <= size.Height;

    private static CoreRect AnnotationBounds(Shape shape, EditorViewModel viewModel)
    {
        var scale = shape.Size.Scale();
        var strokePad = Math.Max(1, viewModel.Settings.StrokeWidth * scale) / 2 + 1;
        return shape.Kind switch
        {
            ShapeKind.Counter => new CoreRect(
                shape.End.X - 13 * scale,
                shape.End.Y - 13 * scale,
                26 * scale,
                26 * scale),
            ShapeKind.Text text => TextBounds(shape.Start, text),
            ShapeKind.Pixelate => shape.Bounds,
            ShapeKind.Arrow => Expand(shape.Bounds, Math.Max(strokePad, 26 * scale)),
            _ => Expand(shape.Bounds, strokePad),
        };
    }

    private static CoreRect TextBounds(CorePoint start, ShapeKind.Text text)
    {
        var lines = text.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var maxCharacters = lines.Length == 0 ? 0 : lines.Max(line => line.Length);
        return new CoreRect(
            start.X,
            start.Y,
            Math.Max(text.Size, maxCharacters * text.Size),
            Math.Max(text.Size, lines.Length * text.Size * 1.5));
    }

    private static CoreRect Expand(CoreRect bounds, double padding) =>
        new(
            bounds.X - padding,
            bounds.Y - padding,
            bounds.Width + padding * 2,
            bounds.Height + padding * 2);

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

        var rect = Normalize(ToPoint(start), ToPoint(end));
        if (viewModel.ActiveTool == EditorTool.Crop)
        {
            DrawRegionPreview(context, rect, viewModel.Zoom);
            return;
        }

        if (viewModel.ActiveTool == EditorTool.Pixelate)
        {
            DrawPixelate(context, rect, exportScale: 1);
            DrawRegionPreview(context, rect, viewModel.Zoom);
            return;
        }

        var preview = new Shape(
            viewModel.ActiveTool == EditorTool.Counter
                ? new ShapeKind.Counter(viewModel.Document?.NextCounterNumber ?? 1)
                : viewModel.ActiveTool == EditorTool.Rectangle
                    ? new ShapeKind.Rectangle()
                    : new ShapeKind.Arrow(),
            viewModel.ColorIndex,
            viewModel.ActiveSize,
            start,
            end);
        DrawShape(context, preview, viewModel, zoom: 1);
    }

    public void CommitTextEditing()
    {
        if (textEditor is not { } editor)
        {
            return;
        }

        var value = editor.Text.Trim();
        var anchor = textEditorAnchor;
        RemoveTextEditor();
        Focus();

        if (ViewModel is null || anchor is null || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        ViewModel.CommitText(anchor.Value, value);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void CancelTextEditing()
    {
        RemoveTextEditor();
        Focus();
        InvalidateVisual();
    }

    private void BeginTextEditing(WpfPoint windowPoint)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        EnsureImageRect();
        var anchor = WindowPointToImagePoint(windowPoint);
        var editor = new WpfTextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = new SolidColorBrush(viewModel.ActiveColor),
            Foreground = new SolidColorBrush(viewModel.ActiveColor),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = viewModel.Settings.TextSize * viewModel.Zoom,
            FontWeight = FontWeights.Bold,
            MinWidth = 60,
            MinHeight = Math.Ceiling(viewModel.Settings.TextSize * viewModel.Zoom * 1.5),
            Padding = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            Width = Math.Max(60, imageRect.Right - windowPoint.X),
        };
        editor.KeyDown += TextEditor_KeyDown;
        editor.TextChanged += TextEditor_TextChanged;

        SetLeft(editor, windowPoint.X);
        SetTop(editor, windowPoint.Y);
        Children.Add(editor);
        textEditor = editor;
        textEditorAnchor = anchor;
        editor.Focus();
        Keyboard.Focus(editor);
        SizeTextEditorToContent(editor);
    }

    private void TextEditor_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CommitTextEditing();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var commit = ViewModel?.Settings.TextEnterBehavior == TextEnterBehavior.Newline
            ? shift
            : !shift;
        if (commit)
        {
            CommitTextEditing();
            e.Handled = true;
        }
    }

    private void TextEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is WpfTextBox editor)
        {
            SizeTextEditorToContent(editor);
        }
    }

    private void SizeTextEditorToContent(WpfTextBox editor)
    {
        editor.Measure(new WpfSize(editor.Width, double.PositiveInfinity));
        editor.Height = Math.Max(editor.MinHeight, Math.Ceiling(editor.DesiredSize.Height));
    }

    private void RemoveTextEditor()
    {
        if (textEditor is not { } editor)
        {
            return;
        }

        editor.KeyDown -= TextEditor_KeyDown;
        editor.TextChanged -= TextEditor_TextChanged;
        Children.Remove(editor);
        textEditor = null;
        textEditorAnchor = null;
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

    private CorePoint AdjustImageEndPoint(CorePoint start, CorePoint end, ModifierKeys modifiers)
    {
        if (ViewModel is null || !modifiers.HasFlag(ModifierKeys.Shift))
        {
            return end;
        }

        return ViewModel.ActiveTool switch
        {
            EditorTool.Rectangle => ConstrainToSquare(start, end),
            EditorTool.Arrow => SnapTo45Degrees(start, end),
            _ => end,
        };
    }

    private void ExpandCanvasForDrag(ref CorePoint start, ref CorePoint end)
    {
        if (ViewModel?.Document is not { } document || !ViewModel.Settings.AutoExpandCanvas)
        {
            return;
        }

        var rect = ExpansionRect(ViewModel.ActiveTool, start, end, ViewModel.StrokeWidth);
        var expansion = CanvasExpansion.Fitting(rect, document.Size);
        if (!expansion.HasWork)
        {
            return;
        }

        var previousImageRect = imageRect;
        var args = new CanvasExpansionRequestedEventArgs(expansion);
        CanvasExpansionRequested?.Invoke(this, args);
        if (!args.Applied)
        {
            return;
        }

        var offset = args.Offset;
        PreserveViewportAnchor(previousImageRect, args.AppliedExpansion);
        if (offset != default)
        {
            start = new CorePoint(start.X + offset.X, start.Y + offset.Y);
            end = new CorePoint(end.X + offset.X, end.Y + offset.Y);
        }

        EnsureImageRect();
    }

    private void PreserveViewportAnchor(Rect previousImageRect, CanvasExpansion expansion)
    {
        if (ViewModel is null)
        {
            return;
        }

        EnsureImageRect();
        var desiredX = previousImageRect.X - expansion.Left * ViewModel.Zoom;
        var desiredY = previousImageRect.Y - expansion.Top * ViewModel.Zoom;
        ViewModel.SetPan(
            ViewModel.PanOffsetX + desiredX - imageRect.X,
            ViewModel.PanOffsetY + desiredY - imageRect.Y);
        EnsureImageRect();
    }

    private static CoreRect ExpansionRect(EditorTool tool, CorePoint start, CorePoint end, double strokeWidth)
    {
        var minX = Math.Min(start.X, end.X);
        var minY = Math.Min(start.Y, end.Y);
        var maxX = Math.Max(start.X, end.X);
        var maxY = Math.Max(start.Y, end.Y);
        var drawingPad = tool switch
        {
            EditorTool.Rectangle or EditorTool.Pixelate or EditorTool.Arrow => Math.Ceiling(Math.Max(strokeWidth, 1) / 2) + 1,
            EditorTool.Counter => 13 * 2.8,
            _ => 0,
        };
        return new CoreRect(
            minX - drawingPad,
            minY - drawingPad,
            maxX - minX + drawingPad * 2,
            maxY - minY + drawingPad * 2);
    }

    private void DrawPixelate(DrawingContext context, Rect rect, double exportScale)
    {
        if (SourceBitmap is null || rect.Width <= 1 || rect.Height <= 1)
        {
            return;
        }

        var sourceRect = new Rect(
            rect.X * SourcePixelsPerDipX,
            rect.Y * SourcePixelsPerDipY,
            rect.Width * SourcePixelsPerDipX,
            rect.Height * SourcePixelsPerDipY);
        var clipped = Rect.Intersect(sourceRect, new Rect(0, 0, SourceBitmap.PixelWidth, SourceBitmap.PixelHeight));
        if (clipped.IsEmpty || !PixelateRenderer.TryGetPixelBounds(clipped, SourceBitmap, out var bounds))
        {
            return;
        }

        var blockSize = Math.Max(1, (int)Math.Round(9 * Math.Max(SourcePixelsPerDipX, SourcePixelsPerDipY)));
        var bitmap = pixelateRenderer.Render(SourceBitmap, bounds, blockSize, exportScale);
        context.DrawImage(bitmap, new Rect(
            bounds.X / SourcePixelsPerDipX,
            bounds.Y / SourcePixelsPerDipY,
            bounds.Width / SourcePixelsPerDipX,
            bounds.Height / SourcePixelsPerDipY));
    }

    private void DrawRegionPreview(DrawingContext context, Rect rect, double zoom)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var thickness = Math.Max(1, 1.5 / Math.Max(0.1, zoom));
        var blackPen = CreateRegionPreviewPen(WpfBrushes.Black, thickness, regionAntPhase);
        var whitePen = CreateRegionPreviewPen(WpfBrushes.White, thickness, regionAntPhase + 4);
        context.DrawRectangle(null, blackPen, rect);
        context.DrawRectangle(null, whitePen, rect);
    }

    private static WpfPen CreateRegionPreviewPen(WpfBrush brush, double thickness, double phase) =>
        new(brush, thickness)
        {
            DashStyle = new DashStyle(RegionPreviewDashPattern, phase),
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
            DashCap = PenLineCap.Square,
            LineJoin = PenLineJoin.Miter,
        };

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
            EditorTool.Text => false,
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

    private Rect ImageRect(CoreSize documentSize, WpfSize available)
    {
        var zoom = ViewModel?.Zoom ?? 1;
        var width = documentSize.Width * zoom;
        var height = documentSize.Height * zoom;
        var centeredX = Math.Max(0, (available.Width - width) / 2);
        var centeredY = Math.Max(0, (available.Height - height) / 2);
        return new Rect(
            centeredX + (ViewModel?.PanOffsetX ?? 0),
            centeredY + (ViewModel?.PanOffsetY ?? 0),
            width,
            height);
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
            imageRect = ImageRect(document.Size, new WpfSize(width, height));
        }
    }

    private CorePoint ClampToDocument(CorePoint point)
    {
        if (ViewModel?.Document is not { } document)
        {
            return point;
        }

        return new CorePoint(
            Math.Clamp(point.X, 0, document.Size.Width),
            Math.Clamp(point.Y, 0, document.Size.Height));
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

    public static double ResolveExportScale(int requestedScale, double sourcePixelsPerDip) =>
        Math.Min(requestedScale == 1 ? 1 : 2, Math.Max(1, NormalizeSourceScale(sourcePixelsPerDip)));

    private static double NormalizeSourceScale(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1;

    private static void SourceBitmapChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is EditorCanvas canvas)
        {
            canvas.pixelateRenderer.Clear();
        }
    }

    private static void ViewModelChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not EditorCanvas canvas)
        {
            return;
        }

        if (canvas.subscribedViewModel is not null)
        {
            canvas.subscribedViewModel.PropertyChanged -= canvas.ViewModel_PropertyChanged;
        }

        canvas.subscribedViewModel = eventArgs.NewValue as EditorViewModel;
        if (canvas.subscribedViewModel is not null)
        {
            canvas.subscribedViewModel.PropertyChanged += canvas.ViewModel_PropertyChanged;
        }

        canvas.UpdateToolCursor();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorViewModel.ActiveTool))
        {
            if (ViewModel?.ActiveTool != EditorTool.Text)
            {
                CommitTextEditing();
            }

            UpdateToolCursor();
            if (!IsRegionPreviewTool(ViewModel?.ActiveTool))
            {
                StopRegionPreviewAnimation();
            }
        }
    }

    private void BeginPan(WpfPoint point)
    {
        if (ViewModel is null)
        {
            return;
        }

        isPanning = true;
        panStart = point;
        panStartOffset = new WpfPoint(ViewModel.PanOffsetX, ViewModel.PanOffsetY);
        CaptureMouse();
        UpdateToolCursor();
    }

    private void EndPan()
    {
        isPanning = false;
        panStart = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        UpdateToolCursor();
    }

    private void UpdateToolCursor()
    {
        Cursor = isPanning || IsPanGestureActive
            ? WpfCursors.Hand
            : ViewModel?.ActiveTool == EditorTool.Text
                ? WpfCursors.IBeam
                : WpfCursors.Cross;
    }

    private static bool IsPanGestureActive => Keyboard.Modifiers == ModifierKeys.None && Keyboard.IsKeyDown(Key.Space);

    private static bool IsZoomWheelGesture =>
        Keyboard.Modifiers == ModifierKeys.Alt || Keyboard.Modifiers == ModifierKeys.Control;

    private void StartRegionPreviewAnimation()
    {
        if (!IsRegionPreviewTool(ViewModel?.ActiveTool))
        {
            return;
        }

        regionAntPhase = 0;
        if (!regionAntTimer.IsEnabled)
        {
            regionAntTimer.Start();
        }
    }

    private void StopRegionPreviewAnimation()
    {
        if (regionAntTimer.IsEnabled)
        {
            regionAntTimer.Stop();
        }
    }

    private static bool IsRegionPreviewTool(EditorTool? tool) =>
        tool is EditorTool.Crop or EditorTool.Pixelate;
}

public sealed class CanvasExpansionRequestedEventArgs(CanvasExpansion expansion) : EventArgs
{
    public CanvasExpansion Expansion { get; } = expansion;

    public CanvasExpansion AppliedExpansion { get; private set; } = expansion;

    public CorePoint Offset => AppliedExpansion.Offset;

    public bool Applied { get; private set; }

    public void MarkApplied(CanvasExpansion? appliedExpansion = null)
    {
        AppliedExpansion = appliedExpansion ?? Expansion;
        Applied = true;
    }
}
