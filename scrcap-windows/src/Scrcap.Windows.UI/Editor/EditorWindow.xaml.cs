using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using Microsoft.Win32;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.Windows.UI.Editor;

public partial class EditorWindow : Window
{
    private readonly EditorViewModel viewModel;
    private readonly Settings settings;
    private readonly List<BitmapSource> bitmapHistory = [];
    private BitmapSource? sourceBitmap;

    public EditorWindow(CaptureResult? capture = null, Settings? settings = null)
    {
        this.settings = settings ?? Settings.Defaults();
        viewModel = new EditorViewModel(this.settings);
        InitializeComponent();
        DataContext = viewModel;
        if (System.Windows.Application.Current is { } app)
        {
            AppThemeService.Apply(app.Resources, this.settings.ThemeMode);
        }

        Canvas.CropCommitted += Canvas_CropCommitted;
        Canvas.DocumentChanged += (_, _) =>
        {
            RecordBitmapForDocumentCursor();
            Canvas.InvalidateVisual();
        };
        CleanupOldDragFiles();

        if (capture is not null)
        {
            LoadBitmap(DecodePng(capture.PngBytes));
            Width = Math.Min(1200, Math.Max(720, capture.PixelWidth + 80));
            Height = Math.Min(900, Math.Max(520, capture.PixelHeight + 120));
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
        if (sourceBitmap is null || !viewModel.Crop(rect))
        {
            return;
        }

        var crop = new Int32Rect(
            Math.Clamp((int)Math.Round(rect.X), 0, Math.Max(0, sourceBitmap.PixelWidth - 1)),
            Math.Clamp((int)Math.Round(rect.Y), 0, Math.Max(0, sourceBitmap.PixelHeight - 1)),
            Math.Clamp((int)Math.Round(rect.Width), 1, sourceBitmap.PixelWidth),
            Math.Clamp((int)Math.Round(rect.Height), 1, sourceBitmap.PixelHeight));
        crop.Width = Math.Min(crop.Width, sourceBitmap.PixelWidth - crop.X);
        crop.Height = Math.Min(crop.Height, sourceBitmap.PixelHeight - crop.Y);
        LoadBitmap(new CroppedBitmap(sourceBitmap, crop), preserveDocument: true);
        RecordBitmapForDocumentCursor();
        Canvas.InvalidateVisual();
    }

    private void LoadBitmap(BitmapSource bitmap, bool preserveDocument = false)
    {
        sourceBitmap = bitmap;
        Canvas.SourceBitmap = bitmap;
        if (!preserveDocument)
        {
            viewModel.LoadDocument(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmapHistory.Clear();
            RecordBitmapForDocumentCursor();
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

    private void TextValue_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (settings.TextEnterBehavior == TextEnterBehavior.Newline)
        {
            var caret = TextValue.CaretIndex;
            TextValue.Text = TextValue.Text.Insert(caret, Environment.NewLine);
            TextValue.CaretIndex = caret + Environment.NewLine.Length;
        }
        else
        {
            Canvas.Focus();
        }

        e.Handled = true;
    }

    private void SelectTool(EditorTool tool)
    {
        viewModel.ActiveTool = tool;
        if (tool == EditorTool.Text)
        {
            TextValue.Focus();
            TextValue.SelectAll();
            return;
        }

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
        if (settings.EscBehavior == EscBehavior.CopyAndClose)
        {
            CopyFlattened();
        }

        Close();
    }

    private void CopyFlattened()
    {
        var bytes = Canvas.FlattenPng(settings.ResolvedExportScale);
        if (bytes.Length == 0)
        {
            return;
        }

        System.Windows.Clipboard.SetImage(DecodePng(bytes));
    }

    private void SaveConfiguredAndClose()
    {
        var folder = settings.SaveFolder;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FilenameGenerator.Filename(settings.FilenamePattern, DateTimeOffset.Now));
        File.WriteAllBytes(path, Canvas.FlattenPng(settings.ResolvedExportScale));
        Close();
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
            File.WriteAllBytes(dialog.FileName, Canvas.FlattenPng(settings.ResolvedExportScale));
            Close();
        }
    }

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
    }
}
