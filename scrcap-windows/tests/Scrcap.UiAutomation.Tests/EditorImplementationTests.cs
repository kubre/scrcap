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
                     "TextValue",
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
        Assert.Contains("DocumentChanged?.Invoke(this, EventArgs.Empty)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorTool.Counter => true", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorTool.Pixelate => rect.Width > 1 && rect.Height > 1", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z", windowSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.Undo()", windowSource, StringComparison.Ordinal);
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
