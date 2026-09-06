using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scrcap.Core;
using Scrcap.Windows.UI.Editor;

namespace Scrcap.Rendering.Tests;

public sealed class EditorAuditTests
{
    [Fact]
    public void NativeSizeUnannotatedExportReusesSourceAndPreservesAlpha() => WpfTestHost.Run(() =>
    {
        var (canvas, _) = CreateCanvas();
        Assert.Same(canvas.SourceBitmap, canvas.FlattenBitmap(1));
        Assert.True(canvas.FlattenBitmap(1).IsFrozen);
        Assert.Equal(0, ReadPixels(canvas.FlattenBitmap(1))[3]);
    });

    [Fact]
    public void AnnotatedAndScaledExportsDoNotFillTransparentPixelsWhite() => WpfTestHost.Run(() =>
    {
        var (canvas, viewModel) = CreateCanvas();
        viewModel.ActiveTool = EditorTool.Rectangle;
        viewModel.CommitShape(new CorePoint(8, 8), new CorePoint(20, 20));
        var image = canvas.FlattenBitmap(1);
        Assert.Equal(0, ReadPixels(image)[3]);
        Assert.Contains(ReadPixels(image), value => value != 0);
    });

    [Theory]
    [InlineData(ShapeSize.Small)]
    [InlineData(ShapeSize.Medium)]
    [InlineData(ShapeSize.Large)]
    public void ExportCommitsPendingTextWithSpacingAndSelectedSize(ShapeSize size) => WpfTestHost.Run(() =>
    {
        var (canvas, viewModel) = CreateCanvas();
        viewModel.ActiveTool = EditorTool.Text;
        viewModel.ActiveSize = size;
        canvas.Measure(new Size(100, 100));
        canvas.Arrange(new Rect(0, 0, 100, 100));
        typeof(EditorCanvas).GetMethod("BeginTextEditing", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(canvas, [new Point(2, 2)]);
        var editor = Assert.IsType<System.Windows.Controls.TextBox>(canvas.Children[0]);
        Assert.Equal(viewModel.TextSize * viewModel.Zoom, editor.FontSize);
        editor.Text = "  indented\nline  ";
        _ = canvas.FlattenBitmap(1);
        var text = Assert.IsType<ShapeKind.Text>(Assert.Single(viewModel.VisibleShapes).Kind);
        Assert.Equal("  indented\nline  ", text.Value);
        Assert.Equal(viewModel.Settings.TextSize * size.Scale(), text.Size);
        Assert.False(canvas.IsTextEditing);
    });

    [Fact]
    public void FitZoomFitsLongCapturesAndZoomInUsesNextLargerStep() => WpfTestHost.Run(() =>
    {
        var viewModel = new EditorViewModel(Settings.Defaults());
        viewModel.LoadDocument(1000, 20000);
        viewModel.FitZoom(800, 600);
        Assert.True(viewModel.Document!.Size.Height * viewModel.Zoom <= 600);
        Assert.Equal(0.027, viewModel.Zoom, precision: 6);
        viewModel.ZoomOut();
        Assert.Equal(0.027, viewModel.Zoom, precision: 6);
        viewModel.ZoomIn();
        Assert.Equal(0.25, viewModel.Zoom);
    });

    [Fact]
    public void CacheRetainsEveryLiveRegionAndPrunesObsoletePreviews() => WpfTestHost.Run(() =>
    {
        var (canvas, _) = CreateCanvas();
        var source = canvas.SourceBitmap!;
        var renderer = new PixelateRenderer();
        // 25 active regions reproduces the old clear-all threshold of 24.
        var bounds = Enumerable.Range(0, 25).Select(x => new Int32Rect(x, 0, 1, 4)).ToArray();
        renderer.BeginFrame();
        var first = bounds.Select(rect => renderer.Render(source, rect, 2, 1)).ToArray();
        renderer.EndFrame();
        renderer.BeginFrame();
        for (var index = 0; index < bounds.Length; index++)
        {
            Assert.Same(first[index], renderer.Render(source, bounds[index], 2, 1));
        }
        renderer.EndFrame();
        Assert.Equal(25, renderer.CachedRegionCount);
        renderer.BeginFrame();
        Assert.Same(first[0], renderer.Render(source, bounds[0], 2, 1));
        renderer.EndFrame();
        Assert.Equal(1, renderer.CachedRegionCount);
    });

    [Fact]
    public void SaveEncodesOffDispatcherAndKeepsNewerEdits() => WpfTestHost.Run(() =>
    {
        var window = new EditorWindow(settings: Settings.Defaults());
        // Show completes the Canvas ViewModel binding, just as in the real UI.
        window.Show();
        window.UpdateLayout();
        var closed = false;
        window.Closed += (_, _) => closed = true;
        var dispatcherThread = Environment.CurrentManagedThreadId;
        var writerThread = dispatcherThread;
        byte[]? written = null;
        var task = window.SaveDocumentAsync(bytes =>
        {
            writerThread = Environment.CurrentManagedThreadId;
            written = bytes;
            return "synthetic-save.png";
        });
        var viewModel = Assert.IsType<EditorViewModel>(window.DataContext);
        viewModel.CommitShape(new CorePoint(2, 2), new CorePoint(30, 30));
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
        Assert.NotEqual(dispatcherThread, writerThread);
        Assert.NotNull(written);
        Assert.NotEmpty(written);
        Assert.False(closed);
        window.Close();
    });

    private static (EditorCanvas, EditorViewModel) CreateCanvas()
    {
        var viewModel = new EditorViewModel(Settings.Defaults());
        viewModel.LoadDocument(32, 32);
        var source = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, new byte[32 * 32 * 4], 32 * 4);
        source.Freeze();
        return (new EditorCanvas { ViewModel = viewModel, SourceBitmap = source }, viewModel);
    }

    private static byte[] ReadPixels(BitmapSource image)
    {
        var normalized = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        normalized.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }
}
