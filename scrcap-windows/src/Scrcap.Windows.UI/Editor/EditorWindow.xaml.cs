using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Scrcap.Core;
using Scrcap.Core.Diagnostics;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Resources;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfTextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;

namespace Scrcap.Windows.UI.Editor;

public partial class EditorWindow : Window
{
    private readonly EditorViewModel viewModel;
    private Settings settings;
    private readonly List<BitmapSource> bitmapHistory = [];
    private BitmapSource? sourceBitmap;
    private bool isSpacePanning;
    private System.Windows.Point panStartPoint;
    private double panStartHorizontalOffset;
    private double panStartVerticalOffset;
    private WpfTextBox? inlineTextBox;
    private CorePoint inlineTextAnchor;

    public EditorWindow(CaptureResult? capture = null, Settings? settings = null, DiagnosticSpan? firstInteractiveRenderSpan = null)
    {
        this.settings = settings ?? Settings.Defaults();
        viewModel = new EditorViewModel(this.settings);
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, this.settings.ThemeMode);
        }

        InitializeComponent();
        DataContext = viewModel;

        Canvas.CropCommitted += Canvas_CropCommitted;
        Canvas.TextRequested += Canvas_TextRequested;
        Canvas.AutoExpanded += (_, growth) => ExpandSourceBitmap(growth);
        Canvas.DragOutRequested += (_, _) => DragOutFlattened();
        Canvas.DocumentChanged += (_, _) =>
        {
            RecordBitmapForDocumentCursor();
            Canvas.InvalidateVisual();
        };
        CleanupOldDragFiles();

        if (capture is not null)
        {
            var logicalWidth = LogicalDimension(capture.PixelWidth, capture.Metadata.DpiScaleX);
            var logicalHeight = LogicalDimension(capture.PixelHeight, capture.Metadata.DpiScaleY);
            LoadBitmap(BitmapSourceFromPixels(capture.Pixels), logicalWidth, logicalHeight);
            Width = Math.Min(1200, Math.Max(720, logicalWidth + 80));
            Height = Math.Min(900, Math.Max(520, logicalHeight + 120));
        }
        else
        {
            LoadBitmap(CreateBlankBitmap(900, 520));
        }

        Loaded += async (_, _) =>
        {
            Canvas.Focus();
            await Dispatcher.InvokeAsync(() =>
            {
                viewModel.FitZoom(Math.Max(1, CanvasScroller.ViewportWidth - 24), Math.Max(1, CanvasScroller.ViewportHeight - 24));
                Canvas.InvalidateVisual();
            }, DispatcherPriority.Loaded);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            firstInteractiveRenderSpan?.Dispose();
        };
        Closed += (_, _) => firstInteractiveRenderSpan?.Dispose();
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (inlineTextBox is not null || e.OriginalSource is WpfTextBox)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
        {
            if (viewModel.Undo())
            {
                RestoreBitmapForDocumentCursor();
            }

            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            if (viewModel.Redo())
            {
                RestoreBitmapForDocumentCursor();
            }

            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        {
            CopyFlattened();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveConfiguredAndClose();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            SaveAsAndClose();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            viewModel.ZoomIn();
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            viewModel.ZoomOut();
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D0)
        {
            viewModel.ResetZoom();
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Done();
            e.Handled = true;
            return;
        }

        var previousTool = viewModel.ActiveTool;
        var previousColor = viewModel.ColorIndex;
        var previousSize = viewModel.ActiveSize;
        var key = e.Key.ToString();
        viewModel.SelectToolByKey(key);
        viewModel.SelectColorByKey(KeyToDigit(e.Key));
        viewModel.SelectSizeByKey(key);
        if (previousTool != viewModel.ActiveTool || previousColor != viewModel.ColorIndex || previousSize != viewModel.ActiveSize)
        {
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void Canvas_CropCommitted(object? sender, CoreRect rect)
    {
        if (sourceBitmap is null || !viewModel.Crop(rect))
        {
            return;
        }

        var crop = new Int32Rect(
            Math.Clamp((int)Math.Round(rect.X * Canvas.SourcePixelScaleX), 0, Math.Max(0, sourceBitmap.PixelWidth - 1)),
            Math.Clamp((int)Math.Round(rect.Y * Canvas.SourcePixelScaleY), 0, Math.Max(0, sourceBitmap.PixelHeight - 1)),
            Math.Clamp((int)Math.Round(rect.Width * Canvas.SourcePixelScaleX), 1, sourceBitmap.PixelWidth),
            Math.Clamp((int)Math.Round(rect.Height * Canvas.SourcePixelScaleY), 1, sourceBitmap.PixelHeight));
        crop.Width = Math.Min(crop.Width, sourceBitmap.PixelWidth - crop.X);
        crop.Height = Math.Min(crop.Height, sourceBitmap.PixelHeight - crop.Y);
        LoadBitmap(new CroppedBitmap(sourceBitmap, crop), preserveDocument: true);
        RecordBitmapForDocumentCursor();
        Canvas.InvalidateVisual();
    }

    private void LoadBitmap(BitmapSource bitmap, bool preserveDocument = false) =>
        LoadBitmap(bitmap, bitmap.PixelWidth, bitmap.PixelHeight, preserveDocument);

    private void LoadBitmap(BitmapSource bitmap, double logicalWidth, double logicalHeight, bool preserveDocument = false)
    {
        sourceBitmap = bitmap;
        Canvas.SourceBitmap = bitmap;
        if (!preserveDocument)
        {
            viewModel.LoadDocument(logicalWidth, logicalHeight);
            bitmapHistory.Clear();
            RecordBitmapForDocumentCursor();
        }

        ApplySourcePixelScale();
    }

    private void ToolArrow_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Arrow);

    private void ToolRectangle_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Rectangle);

    private void ToolCounter_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Counter);

    private void ToolText_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Text);

    private void ToolPixelate_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Pixelate);

    private void ToolCrop_Click(object sender, RoutedEventArgs e) => SelectTool(EditorTool.Crop);

    private void Color1_Click(object sender, RoutedEventArgs e) => SelectColor(0);

    private void Color2_Click(object sender, RoutedEventArgs e) => SelectColor(1);

    private void Color3_Click(object sender, RoutedEventArgs e) => SelectColor(2);

    private void Color4_Click(object sender, RoutedEventArgs e) => SelectColor(3);

    private void Color5_Click(object sender, RoutedEventArgs e) => SelectColor(4);

    private void SizeSmall_Click(object sender, RoutedEventArgs e) => SelectSize(ShapeSize.Small);

    private void SizeMedium_Click(object sender, RoutedEventArgs e) => SelectSize(ShapeSize.Medium);

    private void SizeLarge_Click(object sender, RoutedEventArgs e) => SelectSize(ShapeSize.Large);

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundPoint(-1, CenterOfScroller());
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        ZoomTo(1, CenterOfScroller());
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ZoomAroundPoint(+1, CenterOfScroller());
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Undo();
        RestoreBitmapForDocumentCursor();
        Canvas.InvalidateVisual();
    }

    public void ApplySettings(Settings latestSettings)
    {
        settings = latestSettings;
        viewModel.ApplySettings(latestSettings);
        Canvas.InvalidateVisual();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveConfiguredAndClose();

    private void Done_Click(object sender, RoutedEventArgs e) => Done();

    private void CanvasScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        ZoomAroundPoint(e.Delta > 0 ? +1 : -1, e.GetPosition(CanvasScroller));
        e.Handled = true;
    }

    private void CanvasScroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!Keyboard.IsKeyDown(Key.Space))
        {
            return;
        }

        isSpacePanning = true;
        panStartPoint = e.GetPosition(CanvasScroller);
        panStartHorizontalOffset = CanvasScroller.HorizontalOffset;
        panStartVerticalOffset = CanvasScroller.VerticalOffset;
        CanvasScroller.CaptureMouse();
        Cursor = System.Windows.Input.Cursors.ScrollAll;
        e.Handled = true;
    }

    private void CanvasScroller_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!isSpacePanning)
        {
            return;
        }

        var point = e.GetPosition(CanvasScroller);
        CanvasScroller.ScrollToHorizontalOffset(panStartHorizontalOffset - (point.X - panStartPoint.X));
        CanvasScroller.ScrollToVerticalOffset(panStartVerticalOffset - (point.Y - panStartPoint.Y));
        e.Handled = true;
    }

    private void CanvasScroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!isSpacePanning)
        {
            return;
        }

        isSpacePanning = false;
        CanvasScroller.ReleaseMouseCapture();
        Cursor = null;
        e.Handled = true;
    }

    private void Canvas_TextRequested(object? sender, CorePoint point)
    {
        CommitInlineText();
        BeginInlineText(point);
    }

    private void SelectTool(EditorTool tool)
    {
        CommitInlineText();
        viewModel.ActiveTool = tool;
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void SelectColor(int index)
    {
        CommitInlineText();
        viewModel.ColorIndex = index;
        Canvas.Focus();
    }

    private void SelectSize(ShapeSize size)
    {
        CommitInlineText();
        viewModel.ActiveSize = size;
        Canvas.Focus();
    }

    private void Done()
    {
        CommitInlineText();
        if (settings.EscBehavior == EscBehavior.CopyAndClose && !CopyFlattened())
        {
            return;
        }

        Close();
    }

    private bool CopyFlattened()
    {
        CommitInlineText();
        var bytes = Canvas.FlattenPng(settings.ResolvedExportScale);
        if (bytes.Length == 0)
        {
            return false;
        }

        try
        {
            System.Windows.Clipboard.SetImage(DecodePng(bytes));
            return true;
        }
        catch (Exception ex)
        {
            ShowRecoverableError("scrcap copy failed", ex);
            return false;
        }
    }

    private void SaveConfiguredAndClose()
    {
        CommitInlineText();
        var folder = settings.SaveFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FilenameGenerator.Filename(settings.FilenamePattern, DateTimeOffset.Now));
            File.WriteAllBytes(path, Canvas.FlattenPng(settings.ResolvedExportScale));
            Close();
        }
        catch (Exception ex)
        {
            ShowRecoverableError("scrcap save failed", ex);
        }
    }

    private void SaveAsAndClose()
    {
        CommitInlineText();
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = FilenameGenerator.Filename(settings.FilenamePattern, DateTimeOffset.Now),
            InitialDirectory = string.IsNullOrWhiteSpace(settings.SaveFolder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : settings.SaveFolder,
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                File.WriteAllBytes(dialog.FileName, Canvas.FlattenPng(settings.ResolvedExportScale));
                Close();
            }
            catch (Exception ex)
            {
                ShowRecoverableError("scrcap save failed", ex);
            }
        }
    }

    private void ShowRecoverableError(string title, Exception exception) =>
        System.Windows.MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public static BitmapImage DecodePng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static BitmapSource BitmapSourceFromPixels(CapturedPixels pixels)
    {
        pixels.Validate();
        var bytes = pixels.Bgra32.ToArray();
        var bitmap = BitmapSource.Create(
            pixels.PixelWidth,
            pixels.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            bytes,
            pixels.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static double LogicalDimension(int pixels, double dpiScale) =>
        pixels / Math.Max(0.001, dpiScale);

    public static CapturedPixels CapturedPixelsFromBitmapSource(BitmapSource source, CaptureMetadata metadata)
    {
        BitmapSource bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var bytes = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(bytes, stride, 0);
        return new CapturedPixels(bytes, bitmap.PixelWidth, bitmap.PixelHeight, stride, metadata);
    }

    private static BitmapSource CreateBlankBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = Enumerable.Repeat((byte)255, stride * height).ToArray();
        return BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
    }

    private static string KeyToDigit(Key key) =>
        key switch
        {
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            _ => string.Empty,
        };

    private System.Windows.Point CenterOfScroller() =>
        new(Math.Max(0, CanvasScroller.ViewportWidth / 2), Math.Max(0, CanvasScroller.ViewportHeight / 2));

    private void ZoomAroundPoint(int direction, System.Windows.Point point)
    {
        PreservePointerAcrossZoom(point, () =>
        {
            if (direction > 0)
            {
                viewModel.ZoomIn();
            }
            else
            {
                viewModel.ZoomOut();
            }
        });
    }

    private void ZoomTo(double zoom, System.Windows.Point point)
    {
        PreservePointerAcrossZoom(point, () => viewModel.Zoom = zoom);
    }

    private void PreservePointerAcrossZoom(System.Windows.Point point, Action changeZoom)
    {
        var pointOnCanvas = CanvasScroller.TranslatePoint(point, Canvas);
        var documentX = pointOnCanvas.X / Math.Max(0.001, viewModel.Zoom);
        var documentY = pointOnCanvas.Y / Math.Max(0.001, viewModel.Zoom);
        changeZoom();
        Canvas.Focus();
        Canvas.InvalidateVisual();
        Dispatcher.BeginInvoke(() =>
        {
            Canvas.UpdateLayout();
            var nextCanvasPoint = new System.Windows.Point(documentX * viewModel.Zoom, documentY * viewModel.Zoom);
            var nextScrollerPoint = Canvas.TranslatePoint(nextCanvasPoint, CanvasScroller);
            CanvasScroller.ScrollToHorizontalOffset(CanvasScroller.HorizontalOffset + nextScrollerPoint.X - point.X);
            CanvasScroller.ScrollToVerticalOffset(CanvasScroller.VerticalOffset + nextScrollerPoint.Y - point.Y);
        }, DispatcherPriority.Loaded);
    }

    private static void CleanupOldDragFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "scrcap-drag");
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            try
            {
                if (File.GetCreationTimeUtc(file) < DateTime.UtcNow.AddHours(-24))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private void RecordBitmapForDocumentCursor()
    {
        if (sourceBitmap is null || viewModel.Document is null)
        {
            return;
        }

        while (bitmapHistory.Count > viewModel.Document.Cursor)
        {
            bitmapHistory.RemoveAt(bitmapHistory.Count - 1);
        }

        if (bitmapHistory.Count == viewModel.Document.Cursor)
        {
            bitmapHistory.Add(sourceBitmap);
        }
        else
        {
            bitmapHistory[viewModel.Document.Cursor] = sourceBitmap;
        }
    }

    private void RestoreBitmapForDocumentCursor()
    {
        if (viewModel.Document is null || viewModel.Document.Cursor < 0 || viewModel.Document.Cursor >= bitmapHistory.Count)
        {
            return;
        }

        sourceBitmap = bitmapHistory[viewModel.Document.Cursor];
        Canvas.SourceBitmap = sourceBitmap;
        ApplySourcePixelScale();
    }

    private void ApplySourcePixelScale()
    {
        var document = viewModel.Document;
        if (sourceBitmap is null || document is null)
        {
            Canvas.SourcePixelScaleX = 1;
            Canvas.SourcePixelScaleY = 1;
            return;
        }

        Canvas.SourcePixelScaleX = sourceBitmap.PixelWidth / Math.Max(1, document.Size.Width);
        Canvas.SourcePixelScaleY = sourceBitmap.PixelHeight / Math.Max(1, document.Size.Height);
    }

    private void BeginInlineText(CorePoint anchor)
    {
        RemoveInlineTextBox();
        inlineTextAnchor = anchor;
        var color = viewModel.ActiveColor;
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var textBox = new WpfTextBox
        {
            AcceptsReturn = true,
            AcceptsTab = false,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = brush,
            Foreground = brush,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = EditorRenderMetrics.TextFontSize(settings.TextSize) * viewModel.Zoom,
            MinWidth = 12,
            MinHeight = EditorRenderMetrics.TextLineHeight(settings.TextSize) * viewModel.Zoom,
            MaxWidth = Math.Max(24, (viewModel.Document?.Size.Width - anchor.X ?? 240) * viewModel.Zoom),
            Padding = new Thickness(0),
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        System.Windows.Automation.AutomationProperties.SetAutomationId(textBox, "InlineTextEditor");
        textBox.PreviewKeyDown += InlineTextBox_PreviewKeyDown;
        textBox.TextChanged += InlineTextBox_TextChanged;

        var windowPoint = Canvas.ImagePointToWindowPoint(anchor);
        var overlayPoint = Canvas.TranslatePoint(windowPoint, TextOverlay);
        System.Windows.Controls.Canvas.SetLeft(textBox, overlayPoint.X);
        System.Windows.Controls.Canvas.SetTop(textBox, overlayPoint.Y);
        TextOverlay.Children.Add(textBox);
        inlineTextBox = textBox;
        ResizeInlineTextBox(textBox);
        textBox.Focus();
    }

    private void InlineTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CommitInlineText();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (settings.TextEnterBehavior == TextEnterBehavior.Newline)
        {
            if (shift)
            {
                CommitInlineText();
                e.Handled = true;
            }

            return;
        }

        if (shift)
        {
            InsertInlineNewline();
        }
        else
        {
            CommitInlineText();
        }

        e.Handled = true;
    }

    private void InlineTextBox_TextChanged(object sender, WpfTextChangedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            ResizeInlineTextBox(textBox);
        }
    }

    private void InsertInlineNewline()
    {
        if (inlineTextBox is null)
        {
            return;
        }

        var caret = inlineTextBox.CaretIndex;
        inlineTextBox.Text = inlineTextBox.Text.Insert(caret, Environment.NewLine);
        inlineTextBox.CaretIndex = caret + Environment.NewLine.Length;
        ResizeInlineTextBox(inlineTextBox);
    }

    private void ResizeInlineTextBox(WpfTextBox textBox)
    {
        textBox.Measure(new System.Windows.Size(Math.Max(24, textBox.MaxWidth), double.PositiveInfinity));
        textBox.Width = Math.Min(Math.Max(24, textBox.DesiredSize.Width + 2), Math.Max(24, textBox.MaxWidth));
        textBox.Height = Math.Max(EditorRenderMetrics.TextLineHeight(settings.TextSize) * viewModel.Zoom, textBox.DesiredSize.Height + 2);
    }

    private void CommitInlineText()
    {
        if (inlineTextBox is null)
        {
            return;
        }

        var text = inlineTextBox.Text.Trim();
        var maxWidth = inlineTextBox.MaxWidth / Math.Max(0.001, viewModel.Zoom);
        RemoveInlineTextBox();
        if (string.IsNullOrEmpty(text))
        {
            Canvas.Focus();
            return;
        }

        var previousTool = viewModel.ActiveTool;
        viewModel.ActiveTool = EditorTool.Text;
        var growth = viewModel.CommitShape(inlineTextAnchor, inlineTextAnchor, text, maxWidth);
        viewModel.ActiveTool = previousTool;
        if (growth.HasGrowth)
        {
            ExpandSourceBitmap(growth);
        }

        RecordBitmapForDocumentCursor();
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void RemoveInlineTextBox()
    {
        if (inlineTextBox is null)
        {
            return;
        }

        inlineTextBox.PreviewKeyDown -= InlineTextBox_PreviewKeyDown;
        inlineTextBox.TextChanged -= InlineTextBox_TextChanged;
        TextOverlay.Children.Remove(inlineTextBox);
        inlineTextBox = null;
    }

    private void ExpandSourceBitmap(AutoExpandResult growth)
    {
        if (!growth.HasGrowth || sourceBitmap is null)
        {
            return;
        }

        var scaleX = Canvas.SourcePixelScaleX;
        var scaleY = Canvas.SourcePixelScaleY;
        var left = Math.Max(0, (int)Math.Round(growth.Left * scaleX));
        var top = Math.Max(0, (int)Math.Round(growth.Top * scaleY));
        var right = Math.Max(0, (int)Math.Round(growth.Right * scaleX));
        var bottom = Math.Max(0, (int)Math.Round(growth.Bottom * scaleY));
        var oldBitmap = sourceBitmap.Format == PixelFormats.Bgra32
            ? sourceBitmap
            : new FormatConvertedBitmap(sourceBitmap, PixelFormats.Bgra32, null, 0);
        var oldStride = oldBitmap.PixelWidth * 4;
        var oldPixels = new byte[oldStride * oldBitmap.PixelHeight];
        oldBitmap.CopyPixels(oldPixels, oldStride, 0);

        var newWidth = Math.Max(1, oldBitmap.PixelWidth + left + right);
        var newHeight = Math.Max(1, oldBitmap.PixelHeight + top + bottom);
        var newStride = newWidth * 4;
        var newPixels = new byte[newStride * newHeight];
        var fill = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.CanvasExtensionBackgroundHex);
        for (var offset = 0; offset < newPixels.Length; offset += 4)
        {
            newPixels[offset] = fill.B;
            newPixels[offset + 1] = fill.G;
            newPixels[offset + 2] = fill.R;
            newPixels[offset + 3] = 255;
        }

        for (var y = 0; y < oldBitmap.PixelHeight; y++)
        {
            Buffer.BlockCopy(oldPixels, y * oldStride, newPixels, ((y + top) * newStride) + left * 4, oldStride);
        }

        var expanded = BitmapSource.Create(newWidth, newHeight, 96, 96, PixelFormats.Bgra32, null, newPixels, newStride);
        expanded.Freeze();
        sourceBitmap = expanded;
        Canvas.SourceBitmap = expanded;
        ApplySourcePixelScale();
    }

    private void DragOutFlattened()
    {
        CommitInlineText();
        try
        {
            var bytes = Canvas.FlattenPng(settings.ResolvedExportScale);
            if (bytes.Length == 0)
            {
                return;
            }

            var folder = Path.Combine(Path.GetTempPath(), "scrcap-drag");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FilenameGenerator.Filename(settings.FilenamePattern, DateTimeOffset.Now));
            File.WriteAllBytes(path, bytes);
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop(Canvas, data, System.Windows.DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            ShowRecoverableError("scrcap drag failed", ex);
        }
    }
}
