using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Microsoft.Win32;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Resources;
using WpfColor = System.Windows.Media.Color;

namespace Scrcap.Windows.UI.Editor;

public partial class EditorWindow : Window
{
    private readonly EditorViewModel viewModel;
    private Settings settings;
    private readonly Action? copiedToClipboard;
    private readonly List<BitmapSource> bitmapHistory = [];
    private BitmapSource? sourceBitmap;

    public EditorWindow(CaptureResult? capture = null, Settings? settings = null, Action? copiedToClipboard = null)
    {
        this.settings = settings ?? Settings.Defaults();
        this.copiedToClipboard = copiedToClipboard;
        viewModel = new EditorViewModel(this.settings);
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, this.settings.ThemeMode);
        }

        InitializeComponent();
        ChromeWindow.Attach(this);
        DataContext = viewModel;

        Canvas.CropCommitted += Canvas_CropCommitted;
        Canvas.CanvasExpansionRequested += Canvas_CanvasExpansionRequested;
        Canvas.DragOutRequested += Canvas_DragOutRequested;
        Canvas.DocumentChanged += (_, _) =>
        {
            RecordBitmapForDocumentCursor();
            Canvas.InvalidateVisual();
        };
        CleanupOldDragFiles();

        if (capture is not null)
        {
            LoadBitmap(
                BitmapSourceFromPixels(capture.Pixels),
                pixelsPerDipX: capture.Metadata.DpiScaleX,
                pixelsPerDipY: capture.Metadata.DpiScaleY);
            Width = Math.Min(1200, Math.Max(720, capture.PixelWidth / Canvas.SourcePixelsPerDipX + 80));
            Height = Math.Min(900, Math.Max(520, capture.PixelHeight / Canvas.SourcePixelsPerDipY + 120));
        }
        else
        {
            LoadBitmap(CreateBlankBitmap(900, 520));
        }

        Loaded += (_, _) =>
        {
            Canvas.Focus();
            viewModel.FitZoom(Canvas.ActualWidth, Canvas.ActualHeight);
            Canvas.InvalidateVisual();
        };
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Canvas.IsTextEditing)
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

        var key = e.Key.ToString();
        viewModel.SelectToolByKey(key);
        viewModel.SelectColorByKey(KeyToDigit(e.Key));
        viewModel.SelectSizeByKey(key);
        Canvas.InvalidateVisual();
    }

    private void Canvas_CropCommitted(object? sender, CoreRect rect)
    {
        if (sourceBitmap is null)
        {
            return;
        }

        var aligned = EditorRasterGeometry.AlignCrop(
            rect,
            sourceBitmap.PixelWidth,
            sourceBitmap.PixelHeight,
            Canvas.SourcePixelsPerDipX,
            Canvas.SourcePixelsPerDipY);
        if (!viewModel.Crop(aligned.Logical))
        {
            return;
        }

        LoadBitmap(new CroppedBitmap(sourceBitmap, aligned.Pixels), preserveDocument: true);
        RecordBitmapForDocumentCursor();
        Canvas.InvalidateVisual();
    }

    private void Canvas_CanvasExpansionRequested(object? sender, CanvasExpansionRequestedEventArgs e)
    {
        if (sourceBitmap is null || viewModel.Document is null || !e.Expansion.HasWork)
        {
            return;
        }

        var aligned = EditorRasterGeometry.AlignExpansion(
            e.Expansion,
            Canvas.SourcePixelsPerDipX,
            Canvas.SourcePixelsPerDipY);
        var expanded = ExpandedBitmap(sourceBitmap, aligned, settings.CanvasExtensionBackgroundHex);
        if (expanded is null || !viewModel.ExpandCanvas(aligned.Logical))
        {
            return;
        }

        sourceBitmap = expanded;
        Canvas.SourceBitmap = expanded;
        SyncSourceMetrics();
        RecordBitmapForDocumentCursor();
        e.MarkApplied(aligned.Logical);
    }

    private void LoadBitmap(
        BitmapSource bitmap,
        bool preserveDocument = false,
        double pixelsPerDipX = 1,
        double pixelsPerDipY = 1)
    {
        sourceBitmap = bitmap;
        Canvas.SourceBitmap = bitmap;
        if (!preserveDocument)
        {
            pixelsPerDipX = NormalizeSourceScale(pixelsPerDipX);
            pixelsPerDipY = NormalizeSourceScale(pixelsPerDipY);
            viewModel.LoadDocument(bitmap.PixelWidth / pixelsPerDipX, bitmap.PixelHeight / pixelsPerDipY);
            bitmapHistory.Clear();
            RecordBitmapForDocumentCursor();
        }

        SyncSourceMetrics();
    }

    private void Canvas_DragOutRequested(object? sender, EventArgs e)
    {
        var bytes = Canvas.FlattenPng(settings.ResolvedExportScale);
        if (bytes.Length == 0)
        {
            return;
        }

        string? path = null;
        try
        {
            path = DragOutPayload.CreateTempPng(bytes, settings.FilenamePattern, DateTimeOffset.Now);
            var data = DragOutPayload.CreateDataObject(path);
            System.Windows.DragDrop.DoDragDrop(Canvas, data, System.Windows.DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            ShowRecoverableError("scrcap drag failed", ex);
        }
        finally
        {
            if (path is not null)
            {
                DragOutPayload.ScheduleCleanup(path, TimeSpan.FromSeconds(60));
            }
        }
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
        viewModel.ZoomOut();
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ResetZoom();
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        viewModel.ZoomIn();
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        viewModel.Undo();
        RestoreBitmapForDocumentCursor();
        Canvas.InvalidateVisual();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveConfiguredAndClose();

    private void Done_Click(object sender, RoutedEventArgs e) => Done();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void DragSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void SelectTool(EditorTool tool)
    {
        viewModel.ActiveTool = tool;
        Canvas.Focus();
        Canvas.InvalidateVisual();
    }

    private void SelectColor(int index)
    {
        viewModel.ColorIndex = index;
        Canvas.Focus();
    }

    private void SelectSize(ShapeSize size)
    {
        viewModel.ActiveSize = size;
        Canvas.Focus();
    }

    private void Done()
    {
        if (settings.EscBehavior == EscBehavior.CopyAndClose && !CopyFlattened())
        {
            return;
        }

        Close();
    }

    private bool CopyFlattened()
    {
        var bytes = Canvas.FlattenPng(settings.ResolvedExportScale);
        if (bytes.Length == 0)
        {
            return false;
        }

        try
        {
            System.Windows.Clipboard.SetDataObject(EditorClipboard.CreateDataObject(bytes), true);
            copiedToClipboard?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ShowRecoverableError("scrcap copy failed", ex);
            return false;
        }
    }

    public void ApplySettings(Settings updatedSettings)
    {
        settings = updatedSettings ?? throw new ArgumentNullException(nameof(updatedSettings));
        viewModel.ApplySettings(settings);
        Canvas.InvalidateVisual();
    }

    private void SaveConfiguredAndClose()
    {
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
            96 * NormalizeSourceScale(pixels.Metadata.DpiScaleX),
            96 * NormalizeSourceScale(pixels.Metadata.DpiScaleY),
            PixelFormats.Bgra32,
            null,
            bytes,
            pixels.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource? ExpandedBitmap(BitmapSource source, PixelAlignedCanvasExpansion expansion, string fillHex)
    {
        var left = expansion.LeftPixels;
        var top = expansion.TopPixels;
        var right = expansion.RightPixels;
        var bottom = expansion.BottomPixels;
        var width = source.PixelWidth + left + right;
        var height = source.PixelHeight + top + bottom;
        if (width <= source.PixelWidth && height <= source.PixelHeight)
        {
            return null;
        }

        var fill = ColorFromHex(fillHex);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(fill), null, new Rect(0, 0, width, height));
            context.DrawImage(source, new Rect(left, top, source.PixelWidth, source.PixelHeight));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static WpfColor ColorFromHex(string hex)
    {
        try
        {
            return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.White;
        }
        catch (NotSupportedException)
        {
            return Colors.White;
        }
    }

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

    private static void CleanupOldDragFiles() =>
        DragOutPayload.CleanupOldFiles(Path.Combine(Path.GetTempPath(), "scrcap-drag"), DateTime.UtcNow.AddHours(-24));

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
        SyncSourceMetrics();
    }

    private void SyncSourceMetrics()
    {
        if (sourceBitmap is null || viewModel.Document is null)
        {
            return;
        }

        Canvas.SetSourcePixelsPerDip(
            sourceBitmap.PixelWidth / viewModel.Document.Size.Width,
            sourceBitmap.PixelHeight / viewModel.Document.Size.Height);
    }

    private static double NormalizeSourceScale(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1;
}
