using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
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
                     "ZoomOutButton",
                     "ZoomResetButton",
                     "ZoomInButton",
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
        Assert.Contains("var growth = ViewModel.CommitShape(startImage, endImage)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z", windowSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.Undo()", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTextEditorReplacesToolbarTextFieldAndSuspendsWindowHotkeys()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml");
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.DoesNotContain("TextValue", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TextOverlay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetAutomationId(textBox, \"InlineTextEditor\")", source, StringComparison.Ordinal);
        Assert.Contains("Canvas.TextRequested += Canvas_TextRequested", source, StringComparison.Ordinal);
        Assert.Contains("inlineTextBox is not null || e.OriginalSource is WpfTextBox", source, StringComparison.Ordinal);
        Assert.Contains("CommitInlineText()", source, StringComparison.Ordinal);
        Assert.Contains("viewModel.ActiveTool = previousTool", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTextReturnShiftReturnAndEscapeFollowPreferences()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("settings.TextEnterBehavior == TextEnterBehavior.Newline", source, StringComparison.Ordinal);
        Assert.Contains("if (shift)", source, StringComparison.Ordinal);
        Assert.Contains("InsertInlineNewline()", source, StringComparison.Ordinal);
        Assert.Contains("if (e.Key == Key.Escape)", source, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrEmpty(text)", source, StringComparison.Ordinal);
        Assert.Contains("var maxWidth = inlineTextBox.MaxWidth / Math.Max(0.001, viewModel.Zoom)", source, StringComparison.Ordinal);
        Assert.Contains("text, maxWidth", source, StringComparison.Ordinal);
        Assert.Contains("formatted.MaxTextWidth = maxWidth.Value", ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AutoExpandAndCropPreviewUseSharedInteractionHooks()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var windowSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("WindowPointToImagePoint(point, allowOutsideImage: ViewModel?.Settings.AutoExpandCanvas == true)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("ViewModel.Settings.AutoExpandCanvas && ViewModel.ActiveTool != EditorTool.Crop", canvasSource, StringComparison.Ordinal);
        Assert.Contains("AutoExpanded?.Invoke(this, growth)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("DrawHazardSelection(context, crop)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("EditorRenderMetrics.CropHandleSize", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Canvas.AutoExpanded += (_, growth) => ExpandSourceBitmap(growth)", windowSource, StringComparison.Ordinal);
        Assert.Contains("CanvasExtensionBackgroundHex", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CropPreviewRendersHazardSelectionPixels()
    {
        RunSta(() =>
        {
            var viewModel = new EditorViewModel(Settings.Defaults());
            viewModel.LoadDocument(120, 80);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = CreateGradientBitmap(120, 80),
            };

            var png = canvas.RenderCropPreviewPngForTests(new CoreRect(20, 16, 64, 40));
            var bitmap = EditorWindow.DecodePng(png);
            var pixels = CopyBgra(bitmap);

            var red = CountPixelsNear(pixels, bitmap.PixelWidth, 20, 16, 84, 19, Colors.Red);
            var white = CountPixelsNear(pixels, bitmap.PixelWidth, 17, 13, 24, 20, Colors.White);
            var black = CountPixelsNear(pixels, bitmap.PixelWidth, 20, 16, 84, 19, Colors.Black);
            Assert.True(red > 6, $"Expected red hazard-ant pixels, got {red}.");
            Assert.True(white > 8, $"Expected white handle pixels, got {white}.");
            Assert.True(black > 4, $"Expected black hazard-outline pixels, got {black}.");
            Assert.True(IsDimmed(pixels, bitmap.PixelWidth, 6, 6), "Expected outside-crop preview pixel to be dimmed.");
        });
    }

    [Fact]
    public void DragOutUsesFlattenedRendererPathAndRecoverableFailure()
    {
        var canvasSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorCanvas.cs");
        var windowSource = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("DragOutRequested?.Invoke(this, EventArgs.Empty)", canvasSource, StringComparison.Ordinal);
        Assert.Contains("Canvas.DragOutRequested += (_, _) => DragOutFlattened()", windowSource, StringComparison.Ordinal);
        Assert.Contains("Canvas.FlattenPng(settings.ResolvedExportScale)", windowSource, StringComparison.Ordinal);
        Assert.Contains("System.Windows.DataFormats.FileDrop", windowSource, StringComparison.Ordinal);
        Assert.Contains("ShowRecoverableError(\"scrcap drag failed\", ex)", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorUsesSharedWindowChrome()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml");
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml.cs");

        Assert.Contains("chrome:WindowChromeBehavior.Enabled=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<chrome:ScrcapWindowChrome", xaml, StringComparison.Ordinal);
        Assert.Contains("shell:WindowChrome.IsHitTestVisibleInChrome=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DragMove()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseButton", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolbarUsesParityGridPathIconsAndStateBindings()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Editor/EditorWindow.xaml");
        var app = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml");
        var icons = ReadRepoFile("src/Scrcap.Windows.UI/Resources/IconGeometries.xaml");
        var tokens = ReadRepoFile("src/Scrcap.Windows.UI/Resources/ThemeTokens.xaml");

        Assert.Contains("Resources/IconGeometries.xaml", app, StringComparison.Ordinal);
        foreach (var icon in new[]
                 {
                     "IconArrow",
                     "IconRectangle",
                     "IconCounter",
                     "IconText",
                     "IconPixelate",
                     "IconCrop",
                     "IconUndo",
                     "IconSave",
                     "IconZoomOut",
                     "IconZoomIn",
                 })
        {
            Assert.Contains($"x:Key=\"{icon}\"", icons, StringComparison.Ordinal);
            Assert.Contains($"{{StaticResource {icon}}}", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("<Grid.ColumnDefinitions>", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MetricSeparatorHeight", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x2196;", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x25AD;", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x25A6;", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("&#x2317;", xaml, StringComparison.Ordinal);

        Assert.Contains("<sys:Double x:Key=\"MetricToolbarIconBox\">15</sys:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("<Thickness x:Key=\"MetricToolbarButtonPadding\">7,0</Thickness>", tokens, StringComparison.Ordinal);
        Assert.Contains("<sys:Double x:Key=\"MetricSwatchSize\">11</sys:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("<sys:Double x:Key=\"MetricSmallDotSize\">5</sys:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("<sys:Double x:Key=\"MetricMediumDotSize\">9</sys:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("<sys:Double x:Key=\"MetricLargeDotSize\">13</sys:Double>", tokens, StringComparison.Ordinal);

        var activeBindings = Regex.Matches(xaml, "Tag=\"\\{Binding Is(?:ArrowTool|RectangleTool|CounterTool|TextTool|PixelateTool|CropTool|Color[1-5]|SmallSize|MediumSize|LargeSize)Active\\}\"");
        Assert.Equal(14, activeBindings.Count);
    }

    [Fact]
    public void UndoEnabledStateTracksDocumentCursor()
    {
        var viewModel = new EditorViewModel(Settings.Defaults());
        viewModel.LoadDocument(320, 200);

        Assert.False(viewModel.CanUndo);
        viewModel.CommitShape(new CorePoint(10, 10), new CorePoint(80, 40));
        Assert.True(viewModel.CanUndo);
        Assert.True(viewModel.Undo());
        Assert.False(viewModel.CanUndo);
        Assert.True(viewModel.CanRedo);
        Assert.True(viewModel.Redo());
        Assert.True(viewModel.CanUndo);
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

    [Fact]
    public void DragOutWritesPngWithDpiMetadataAndCleansOldTempFiles()
    {
        RunSta(() =>
        {
            EnsureApplication();
            var settings = Settings.Defaults();
            settings.ExportScale = 2;
            settings.FilenamePattern = "drag-proof";
            var source = CreateGradientBitmap(80, 50);
            var metadata = new CaptureMetadata(CaptureMode.Region, "drag-proof.png", null, DateTimeOffset.UnixEpoch);
            var window = new EditorWindow(
                new CaptureResult(EditorWindow.CapturedPixelsFromBitmapSource(source, metadata), metadata),
                settings);
            string? dragPath = null;
            try
            {
                dragPath = window.CreateDragOutPngForTests();
                Assert.True(File.Exists(dragPath));
                using (var stream = File.OpenRead(dragPath))
                {
                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    Assert.Equal(160, frame.PixelWidth);
                    Assert.Equal(100, frame.PixelHeight);
                    Assert.Equal(192, frame.DpiX, precision: 0);
                    Assert.Equal(192, frame.DpiY, precision: 0);
                }

                var oldPath = Path.Combine(Path.GetDirectoryName(dragPath)!, "old-drag-proof.png");
                File.WriteAllBytes(oldPath, File.ReadAllBytes(dragPath));
                File.SetCreationTimeUtc(oldPath, DateTime.UtcNow.AddHours(-25));
                EditorWindow.CleanupOldDragFilesForTests();

                Assert.False(File.Exists(oldPath));
            }
            finally
            {
                window.Close();
                if (dragPath is not null)
                {
                    File.Delete(dragPath);
                }
            }
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

    private static byte[] CopyBgra(BitmapSource source)
    {
        var bitmap = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static int CountPixelsNear(byte[] pixels, int width, int left, int top, int right, int bottom, Color color)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var index = ((y * width) + x) * 4;
                if (index + 3 >= pixels.Length)
                {
                    continue;
                }

                var distance = Math.Abs(pixels[index] - color.B)
                    + Math.Abs(pixels[index + 1] - color.G)
                    + Math.Abs(pixels[index + 2] - color.R);
                if (distance < 80)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool IsDimmed(byte[] pixels, int width, int x, int y)
    {
        var index = ((y * width) + x) * 4;
        return pixels[index] < 230 && pixels[index + 1] < 230 && pixels[index + 2] < 230;
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
        }

        var app = Application.Current ?? throw new InvalidOperationException("WPF application was not created.");
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Resources/ThemeTokens.xaml", UriKind.Relative),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Resources/IconGeometries.xaml", UriKind.Relative),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Chrome/ScrcapWindowChrome.xaml", UriKind.Relative),
            });
        }
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
