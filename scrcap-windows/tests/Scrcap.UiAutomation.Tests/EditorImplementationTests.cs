using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using Scrcap.Windows.UI.Editor;

namespace Scrcap.UiAutomation.Tests;

public sealed class EditorImplementationTests
{
    [Fact]
    public void EditorToolbarExposesAutomationTargetsForEveryToolAndCommand()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml");

        Assert.Contains("WindowStyle=\"None\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<shell:WindowChrome CaptionHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GlassFrameThickness=\"0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UseAeroCaptionButtons=\"False\"", xaml, StringComparison.Ordinal);
        var canvasHostIndex = xaml.IndexOf("<Grid Background=\"{DynamicResource BrushCanvas}\"", StringComparison.Ordinal);
        Assert.True(canvasHostIndex >= 0);
        var editorCanvasIndex = xaml.IndexOf("<editor:EditorCanvas x:Name=\"Canvas\"", canvasHostIndex, StringComparison.Ordinal);
        Assert.True(editorCanvasIndex > canvasHostIndex);
        var editorCanvasEndIndex = xaml.IndexOf("/>", editorCanvasIndex, StringComparison.Ordinal);
        Assert.True(editorCanvasEndIndex > editorCanvasIndex);
        Assert.Contains("ClipToBounds=\"True\"", xaml[canvasHostIndex..editorCanvasIndex], StringComparison.Ordinal);
        Assert.Contains("ClipToBounds=\"True\"", xaml[editorCanvasIndex..editorCanvasEndIndex], StringComparison.Ordinal);
        Assert.Contains("Margin=\"{StaticResource MetricCanvasMargin}\"", xaml[editorCanvasIndex..editorCanvasEndIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("<Border Margin=\"{StaticResource MetricEditorPadding}\"", xaml, StringComparison.Ordinal);

        foreach (var automationId in new[]
                 {
                     "UndoButton",
                     "SaveButton",
                     "DoneButton",
                     "CloseButton",
                     "ToolArrow",
                     "ToolRectangle",
                     "ToolCounter",
                     "ToolText",
                     "ToolPixelate",
                     "ToolCrop",
                     "Color1",
                     "Color2",
                     "Color3",
                     "Color4",
                     "Color5",
                     "SizeSmall",
                     "SizeMedium",
                     "SizeLarge",
                     "ZoomStatus",
                 })
        {
            Assert.Contains($"AutomationProperties.AutomationId=\"{automationId}\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KeyboardShortcutsSelectEveryEditorToolColorAndSize()
    {
        var viewModel = new EditorViewModel(Settings.Defaults());

        foreach (var (key, tool) in new[]
                 {
                     ("Q", EditorTool.Arrow),
                     ("W", EditorTool.Rectangle),
                     ("E", EditorTool.Counter),
                     ("R", EditorTool.Text),
                     ("T", EditorTool.Pixelate),
                     ("Y", EditorTool.Crop),
                 })
        {
            viewModel.SelectToolByKey(key);
            Assert.Equal(tool, viewModel.ActiveTool);
        }

        viewModel.SelectColorByKey("5");
        Assert.Equal(4, viewModel.ColorIndex);

        viewModel.SelectSizeByKey("X");
        Assert.Equal(ShapeSize.Medium, viewModel.ActiveSize);

        viewModel.SelectSizeByKey("C");
        Assert.Equal(ShapeSize.Large, viewModel.ActiveSize);
    }

    [Fact]
    public void LiveEditorSettingsRefreshPaletteStrokeAndDoneBehavior()
    {
        var initial = Settings.Defaults();
        var viewModel = new EditorViewModel(initial);
        viewModel.LoadDocument(320, 200);

        var updated = Settings.Defaults();
        updated.PaletteHex[0] = "#FF00FF";
        updated.StrokeWidth = 9;
        updated.EscBehavior = EscBehavior.CopyAndClose;
        viewModel.ApplySettings(updated);

        Assert.Same(updated, viewModel.Settings);
        Assert.Equal("#FF00FF", viewModel.Palette[0]);
        Assert.Equal(9 * ShapeSize.Small.Scale(), viewModel.StrokeWidth);
        Assert.Equal("Copy & Close", viewModel.DoneText);
        Assert.NotEmpty(viewModel.PaletteBrushes);
    }

    [Fact]
    public void EditorCommitsAllToolsAndUndoRemovesLastInteraction()
    {
        var viewModel = new EditorViewModel(Settings.Defaults());
        viewModel.LoadDocument(320, 200);

        Commit(viewModel, EditorTool.Arrow, new CorePoint(10, 10), new CorePoint(110, 40));
        Commit(viewModel, EditorTool.Rectangle, new CorePoint(30, 50), new CorePoint(140, 120));
        Commit(viewModel, EditorTool.Counter, new CorePoint(160, 70), new CorePoint(160, 70));
        Commit(viewModel, EditorTool.Text, new CorePoint(40, 145), new CorePoint(40, 145), "done");
        Commit(viewModel, EditorTool.Pixelate, new CorePoint(180, 40), new CorePoint(260, 120));

        Assert.Collection(
            viewModel.VisibleShapes,
            shape => Assert.IsType<ShapeKind.Arrow>(shape.Kind),
            shape => Assert.IsType<ShapeKind.Rectangle>(shape.Kind),
            shape =>
            {
                var counter = Assert.IsType<ShapeKind.Counter>(shape.Kind);
                Assert.Equal(1, counter.Number);
            },
            shape =>
            {
                var text = Assert.IsType<ShapeKind.Text>(shape.Kind);
                Assert.Equal("done", text.Value);
            },
            shape => Assert.IsType<ShapeKind.Pixelate>(shape.Kind));

        Assert.True(viewModel.Crop(new CoreRect(0, 0, 240, 160)));
        Assert.Equal(new CoreSize(240, 160), viewModel.Document!.Size);
        Assert.True(viewModel.Undo());
        Assert.Equal(new CoreSize(320, 200), viewModel.Document.Size);

        Assert.True(viewModel.Undo());
        Assert.Equal(4, viewModel.VisibleShapes.Count);
        Assert.DoesNotContain(viewModel.VisibleShapes, shape => shape.Kind is ShapeKind.Pixelate);
    }

    [Fact]
    public void NonTextShapesCommitWithoutEnterAndCtrlZIsWiredToUndo()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var windowSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("ViewModel.CommitShape(startImage, endImage)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("CommitTextEditing()", canvasSource, StringComparison.Ordinal);
        Assert.Contains("BeginTextEditing(e.GetPosition(this))", canvasSource, StringComparison.Ordinal);
        Assert.Contains("DocumentChanged?.Invoke(this, EventArgs.Empty)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorTool.Counter => true", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorTool.Text => false", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorTool.Pixelate => rect.Width > 1 && rect.Height > 1", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z", windowSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.Undo()", windowSource, StringComparison.Ordinal);
        Assert.Contains("Canvas.Focus()", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TextValue.Focus()", windowSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TextValue_KeyDown", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTextEditorUsesMacReturnAndEscapeSemantics()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var viewModelSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorViewModel.cs");

        Assert.Contains("WpfCursors.IBeam", canvasSource, StringComparison.Ordinal);
        Assert.Contains("WpfCursors.Cross", canvasSource, StringComparison.Ordinal);
        Assert.Contains("AcceptsReturn = true", canvasSource, StringComparison.Ordinal);
        Assert.Contains("public bool IsTextEditing => textEditor is not null;", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel?.Settings.TextEnterBehavior == TextEnterBehavior.Newline", canvasSource, StringComparison.Ordinal);
        Assert.Contains("? shift", canvasSource, StringComparison.Ordinal);
        Assert.Contains(": !shift", canvasSource, StringComparison.Ordinal);
        Assert.Contains("if (e.Key == Key.Escape)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("CommitText(anchor.Value, value)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("public void CommitText(CorePoint anchor, string text)", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTextEditorOwnsTypedShortcutKeys()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var windowSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("public bool IsTextEditing => textEditor is not null;", canvasSource, StringComparison.Ordinal);
        Assert.Contains("if (Canvas.IsTextEditing)", windowSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.SelectToolByKey(key);", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CropAndPixelateUseMarchingRegionPreviewInsteadOfAnnotationRectangle()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");

        Assert.Contains("DrawRegionPreview(context, rect, viewModel.Zoom)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("DrawPixelate(context, rect, exportScale: 1)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("WpfBrushes.Black", canvasSource, StringComparison.Ordinal);
        Assert.Contains("WpfBrushes.White", canvasSource, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Render", canvasSource, StringComparison.Ordinal);
        Assert.Contains("IsRegionPreviewTool(EditorTool? tool)", canvasSource, StringComparison.Ordinal);
        Assert.DoesNotContain("viewModel.ActiveTool == EditorTool.Crop\r\n                        ? new ShapeKind.Rectangle()", canvasSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorCanvasSupportsPhotoshopStylePanAndZoomGestures()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var viewModelSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorViewModel.cs");

        Assert.Contains("Keyboard.IsKeyDown(Key.Space)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("BeginPan(e.GetPosition(this))", canvasSource, StringComparison.Ordinal);
        Assert.Contains("IsManipulationEnabled = true", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ManipulationModes.Translate | ManipulationModes.Scale", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PanBy(translation.X, translation.Y)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.PanBy(0, e.Delta)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ZoomAt(direction, position.X, position.Y, ActualWidth, ActualHeight)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ZoomAt(+1, origin.X, origin.Y, ActualWidth, ActualHeight)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.ZoomAt(-1, origin.X, origin.Y, ActualWidth, ActualHeight)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("public void PanBy(double deltaX, double deltaY)", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public void ZoomAt(int direction, double viewportX, double viewportY, double viewportWidth, double viewportHeight)", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AltWheelZoomKeepsThePointUnderTheCursorAnchored()
    {
        var viewModel = new EditorViewModel(Settings.Defaults());
        viewModel.LoadDocument(400, 300);
        viewModel.ResetZoom();

        const double viewportWidth = 800;
        const double viewportHeight = 600;
        const double cursorX = 500;
        const double cursorY = 320;

        var imageXBefore = ImagePointX(viewModel, cursorX, viewportWidth);
        var imageYBefore = ImagePointY(viewModel, cursorY, viewportHeight);

        viewModel.ZoomAt(+1, cursorX, cursorY, viewportWidth, viewportHeight);

        Assert.Equal(1.25, viewModel.Zoom);
        Assert.Equal(imageXBefore, ImagePointX(viewModel, cursorX, viewportWidth), precision: 6);
        Assert.Equal(imageYBefore, ImagePointY(viewModel, cursorY, viewportHeight), precision: 6);

        var panX = viewModel.PanOffsetX;
        var panY = viewModel.PanOffsetY;
        viewModel.PanBy(12, -8);
        Assert.Equal(panX + 12, viewModel.PanOffsetX);
        Assert.Equal(panY - 8, viewModel.PanOffsetY);
    }

    [Fact]
    public void FlattenedPngIncludesAnnotationsWithinRenderingTolerance()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.ExportScale = 1;
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(160, 120);
            Commit(viewModel, EditorTool.Arrow, new CorePoint(8, 8), new CorePoint(120, 30));
            Commit(viewModel, EditorTool.Rectangle, new CorePoint(20, 42), new CorePoint(90, 94));
            Commit(viewModel, EditorTool.Counter, new CorePoint(120, 70), new CorePoint(120, 70));
            Commit(viewModel, EditorTool.Text, new CorePoint(14, 102), new CorePoint(14, 102), "A");
            Commit(viewModel, EditorTool.Pixelate, new CorePoint(96, 36), new CorePoint(150, 96));

            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = CreateGradientBitmap(160, 120),
            };

            var png = canvas.FlattenPng(scale: 1);
            Assert.True(png.Length > 1_000);

            var decoded = EditorWindow.DecodePng(png);
            Assert.Equal(160, decoded.PixelWidth);
            Assert.Equal(120, decoded.PixelHeight);
            Assert.InRange(CountNonWhitePixels(decoded), 2_000, 19_200);
        });
    }

    private static void Commit(EditorViewModel viewModel, EditorTool tool, CorePoint start, CorePoint end, string? text = null)
    {
        viewModel.ActiveTool = tool;
        viewModel.CommitShape(start, end, text);
    }

    private static BitmapSource CreateGradientBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * stride) + (x * 4);
                pixels[offset] = (byte)(x % 256);
                pixels[offset + 1] = (byte)(y % 256);
                pixels[offset + 2] = 240;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static int CountNonWhitePixels(BitmapSource source)
    {
        var bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] < 245 || pixels[i + 1] < 245 || pixels[i + 2] < 245)
            {
                count++;
            }
        }

        return count;
    }

    private static double ImagePointX(EditorViewModel viewModel, double viewportX, double viewportWidth)
    {
        var origin = Math.Max(0, (viewportWidth - viewModel.Document!.Size.Width * viewModel.Zoom) / 2) + viewModel.PanOffsetX;
        return (viewportX - origin) / viewModel.Zoom;
    }

    private static double ImagePointY(EditorViewModel viewModel, double viewportY, double viewportHeight)
    {
        var origin = Math.Max(0, (viewportHeight - viewModel.Document!.Size.Height * viewModel.Zoom) / 2) + viewModel.PanOffsetY;
        return (viewportY - origin) / viewModel.Zoom;
    }

    private static void RunSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Scrcap.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate scrcap-windows root.");
    }
}
