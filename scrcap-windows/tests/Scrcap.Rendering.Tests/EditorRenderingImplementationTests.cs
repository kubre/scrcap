using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Editor;

namespace Scrcap.Rendering.Tests;

public sealed class EditorRenderingImplementationTests
{
    [Fact]
    public void FlattenPngRendersCommittedAnnotations()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#FF0000", "#00AA00", "#0066CC", "#111111", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(96, 64);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(96, 64, Colors.White),
                Width = 96,
                Height = 64,
            };

            viewModel.ActiveTool = EditorTool.Rectangle;
            viewModel.CommitShape(new CorePoint(8, 8), new CorePoint(72, 42));
            viewModel.ActiveTool = EditorTool.Arrow;
            viewModel.CommitShape(new CorePoint(12, 52), new CorePoint(84, 52));
            viewModel.ActiveTool = EditorTool.Counter;
            viewModel.CommitShape(new CorePoint(44, 28), new CorePoint(44, 28));
            viewModel.ActiveTool = EditorTool.Text;
            viewModel.CommitShape(new CorePoint(10, 10), new CorePoint(10, 10), "QA");

            var bytes = canvas.FlattenPng(scale: 1);
            Assert.True(bytes.Length > 100);

            var pixels = DecodeBgra(bytes, out var width, out var height);
            Assert.Equal(96, width);
            Assert.Equal(64, height);
            Assert.True(CountNonWhitePixels(pixels) > 350);
        });
    }

    [Fact]
    public void FlattenPngRendersFiveHundredShapesWithoutPerShapeControls()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.AutoExpandCanvas = false;
            settings.PaletteHex = ["#FF0000", "#00AA00", "#0066CC", "#111111", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(480, 360);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(480, 360, Colors.White),
                Width = 480,
                Height = 360,
            };

            for (var index = 0; index < 500; index++)
            {
                var x = 4 + index % 40 * 12;
                var y = 4 + index / 40 * 24;
                viewModel.ColorIndex = index % settings.PaletteHex.Count;
                viewModel.ActiveSize = index % 3 == 0 ? ShapeSize.Small : index % 3 == 1 ? ShapeSize.Medium : ShapeSize.Large;
                viewModel.ActiveTool = index % 2 == 0 ? EditorTool.Rectangle : EditorTool.Arrow;
                viewModel.CommitShape(new CorePoint(x, y), new CorePoint(Math.Min(476, x + 10), Math.Min(356, y + 12)));
            }

            var bytes = canvas.FlattenPng(scale: 1);
            var pixels = DecodeBgra(bytes, out var width, out var height);

            Assert.Equal(480, width);
            Assert.Equal(360, height);
            Assert.Equal(500, viewModel.VisibleShapes.Count);
            Assert.True(CountNonWhitePixels(pixels) > 5_000);
            Assert.Equal(0, VisualTreeHelper.GetChildrenCount(canvas));
        });
    }

    [Fact]
    public void FlattenPngAppliesPixelationToSourceRegion()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#FF0000", "#00AA00", "#0066CC", "#111111", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(48, 48);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = CheckerBitmap(48, 48),
                Width = 48,
                Height = 48,
            };

            viewModel.ActiveTool = EditorTool.Pixelate;
            viewModel.CommitShape(new CorePoint(4, 4), new CorePoint(40, 40));

            var bytes = canvas.FlattenPng(scale: 1);
            var pixels = DecodeBgra(bytes, out var width, out _);
            var uniqueColorsInRegion = new HashSet<int>();
            for (var y = 8; y < 32; y++)
            {
                for (var x = 8; x < 32; x++)
                {
                    var offset = ((y * width) + x) * 4;
                    uniqueColorsInRegion.Add(BitConverter.ToInt32(pixels, offset));
                }
            }

            Assert.True(uniqueColorsInRegion.Count < 40);
        });
    }

    [Fact]
    public void FlattenPngRendersPixelateWithoutAnnotationBorder()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#FF0000", "#00AA00", "#0066CC", "#111111", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(48, 48);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(48, 48, Colors.White),
                Width = 48,
                Height = 48,
            };

            viewModel.ActiveTool = EditorTool.Pixelate;
            viewModel.CommitShape(new CorePoint(4, 4), new CorePoint(40, 40));

            var pixels = DecodeBgra(canvas.FlattenPng(scale: 1), out _, out _);

            Assert.Equal(0, CountNonWhitePixels(pixels));
        });
    }

    [Fact]
    public void RendererFormulaConstantsMatchSourceVisualModel()
    {
        Assert.Equal(9, EditorRenderMetrics.ArrowHeadLength(20));
        Assert.Equal(18, EditorRenderMetrics.ArrowHeadLength(100));
        Assert.Equal(26, EditorRenderMetrics.ArrowHeadLength(400));
        Assert.Equal(2.4, EditorRenderMetrics.RectangleStroke(8, 2.8, 30, 30), precision: 3);
        Assert.Equal(14, EditorRenderMetrics.CounterFontSize(99, EditorRenderMetrics.CounterRadius));
        Assert.Equal(11, EditorRenderMetrics.CounterFontSize(100, EditorRenderMetrics.CounterRadius));
        Assert.True(EditorRenderMetrics.ShouldUseDarkCounterText(Colors.White));
        Assert.False(EditorRenderMetrics.ShouldUseDarkCounterText(Colors.Red));
        Assert.Equal(17, EditorRenderMetrics.TextFontSize(16));
        Assert.Equal((4, 3), EditorRenderMetrics.PixelateGrid(36, 27));
    }

    [Fact]
    public void PixelateUsesDownsampledBlocksInsteadOfTopLeftReplication()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(18, 18);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = MostlyBlueBitmapWithRedTopLeft(18, 18),
                Width = 18,
                Height = 18,
            };

            viewModel.ActiveTool = EditorTool.Pixelate;
            viewModel.CommitShape(new CorePoint(0, 0), new CorePoint(18, 18));

            var pixels = DecodeBgra(canvas.FlattenPng(scale: 1), out var width, out _);
            var offset = ((4 * width) + 4) * 4;

            Assert.True(pixels[offset] > 180);
            Assert.True(pixels[offset + 2] < 80);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void FlattenPngWrapsTextAtCommittedInlineEditorWidth(int scale)
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#111111", "#00AA00", "#0066CC", "#FF0000", "#FF00FF"];
            settings.TextSize = 16;
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(120, 80);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(120, 80, Colors.White),
                Width = 120,
                Height = 80,
            };

            viewModel.ActiveTool = EditorTool.Text;
            viewModel.CommitShape(new CorePoint(8, 8), new CorePoint(8, 8), "Wrap Wrap Wrap", textMaxWidth: 42);

            var pixels = DecodeBgra(canvas.FlattenPng(scale), out var width, out _);

            Assert.True(CountNonWhitePixelsInRect(pixels, width, 8 * scale, 30 * scale, 70 * scale, 35 * scale) > 0);
        });
    }

    [Fact]
    public void CapturedPixelsRoundTripPreservesSourcePixels()
    {
        RunSta(() =>
        {
            var source = CheckerBitmap(24, 18);
            var metadata = new CaptureMetadata(CaptureMode.Region, null, new PixelRect(2, 4, 24, 18), DateTimeOffset.Now);

            var pixels = EditorWindow.CapturedPixelsFromBitmapSource(source, metadata);
            var roundTrip = EditorWindow.BitmapSourceFromPixels(pixels);
            var sourceBytes = CopyBgra(source);
            var roundTripBytes = CopyBgra(roundTrip);

            Assert.Equal(source.PixelWidth, pixels.PixelWidth);
            Assert.Equal(source.PixelHeight, pixels.PixelHeight);
            Assert.Equal(sourceBytes, roundTripBytes);
            Assert.Equal(metadata, pixels.Metadata);
        });
    }

    [Fact]
    public void BitmapSourceFromPixelsPreservesBgraAndAlphaBytes()
    {
        RunSta(() =>
        {
            var metadata = new CaptureMetadata(CaptureMode.Region, null, new PixelRect(0, 0, 2, 2), DateTimeOffset.Now);
            byte[] bgra =
            [
                10,
                20,
                30,
                40,
                50,
                60,
                70,
                80,
                90,
                100,
                110,
                120,
                130,
                140,
                150,
                160,
            ];

            var bitmap = EditorWindow.BitmapSourceFromPixels(new CapturedPixels(bgra, 2, 2, 8, metadata));
            var output = CopyBgra(bitmap);

            Assert.Equal(bgra, output);
        });
    }

    [Fact]
    public void BitmapSourceFromPixelsRejectsInvalidContracts()
    {
        RunSta(() =>
        {
            var metadata = new CaptureMetadata(CaptureMode.Region, null, null, DateTimeOffset.Now);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EditorWindow.BitmapSourceFromPixels(new CapturedPixels(new byte[4], 0, 1, 4, metadata)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EditorWindow.BitmapSourceFromPixels(new CapturedPixels(new byte[4], 1, 1, 3, metadata)));
            Assert.Throws<ArgumentException>(() =>
                EditorWindow.BitmapSourceFromPixels(new CapturedPixels(new byte[4], 2, 1, 8, metadata)));
        });
    }

    [Fact]
    public void PixelateOutputInvalidatesWhenSourceBitmapChanges()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#FF0000", "#00AA00", "#0066CC", "#111111", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(32, 32);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(32, 32, Colors.Red),
                Width = 32,
                Height = 32,
            };

            viewModel.ActiveTool = EditorTool.Pixelate;
            viewModel.CommitShape(new CorePoint(0, 0), new CorePoint(32, 32));

            var redOutput = DecodeBgra(canvas.FlattenPng(scale: 1), out var width, out _);
            canvas.SourceBitmap = SolidBitmap(32, 32, Colors.Blue);
            var blueOutput = DecodeBgra(canvas.FlattenPng(scale: 1), out _, out _);
            var center = ((16 * width) + 16) * 4;

            Assert.True(redOutput[center + 2] > 200);
            Assert.True(blueOutput[center] > 200);
        });
    }

    [Theory]
    [InlineData(1.0, 300, 180)]
    [InlineData(1.5, 200, 120)]
    [InlineData(2.0, 150, 90)]
    public void DpiScaledDocumentKeepsLogicalSizeButExportsSourcePixels(double sourceScale, int logicalWidth, int logicalHeight)
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            var source = SolidBitmap(300, 180, Colors.White);
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(logicalWidth, logicalHeight);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = source,
                SourcePixelScaleX = sourceScale,
                SourcePixelScaleY = sourceScale,
                Width = logicalWidth,
                Height = logicalHeight,
            };

            Assert.Equal($"{logicalWidth} \u00d7 {logicalHeight}", viewModel.DocumentSizeText);

            var bytes = canvas.FlattenPng(scale: 1);
            _ = DecodeBgra(bytes, out var width, out var height);

            Assert.Equal(300, width);
            Assert.Equal(180, height);
        });
    }

    [Fact]
    public void EditorWindowUsesCaptureDpiForLogicalDocumentAndWindowSize()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var source = SolidBitmap(300, 180, Colors.White);
            var metadata = new CaptureMetadata(
                CaptureMode.Region,
                null,
                new PixelRect(0, 0, 300, 180),
                DateTimeOffset.Now,
                DpiScaleX: 1.5,
                DpiScaleY: 1.5);
            var capture = new CaptureResult(EditorWindow.CapturedPixelsFromBitmapSource(source, metadata), metadata);
            var window = new EditorWindow(capture, Settings.Defaults());
            var viewModel = (EditorViewModel)window.DataContext;

            Assert.Equal(new CoreSize(200, 120), viewModel.Document!.Size);
            Assert.Equal("200 \u00d7 120", viewModel.DocumentSizeText);
            Assert.Equal(720, window.Width);
            Assert.Equal(520, window.Height);
        });
    }

    private static BitmapSource SolidBitmap(int width, int height, Color color)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource CheckerBitmap(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var red = (byte)((x * 5) % 255);
                var green = (byte)((y * 7) % 255);
                pixels[offset] = 220;
                pixels[offset + 1] = green;
                pixels[offset + 2] = red;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource MostlyBlueBitmapWithRedTopLeft(int width, int height)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
            pixels[index + 1] = 0;
            pixels[index + 2] = 0;
            pixels[index + 3] = 255;
        }

        pixels[0] = 0;
        pixels[1] = 0;
        pixels[2] = 255;
        pixels[3] = 255;
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] DecodeBgra(byte[] pngBytes, out int width, out int height)
    {
        using var stream = new MemoryStream(pngBytes);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        width = converted.PixelWidth;
        height = converted.PixelHeight;
        var pixels = new byte[width * height * 4];
        converted.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static byte[] CopyBgra(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static int CountNonWhitePixels(byte[] pixels)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] < 245 || pixels[index + 1] < 245 || pixels[index + 2] < 245)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountNonWhitePixelsInRect(byte[] pixels, int width, int left, int top, int right, int bottom)
    {
        var count = 0;
        for (var y = Math.Max(0, top); y < bottom; y++)
        {
            for (var x = Math.Max(0, left); x < right; x++)
            {
                var index = ((y * width) + x) * 4;
                if (index + 2 >= pixels.Length)
                {
                    continue;
                }

                if (pixels[index] < 245 || pixels[index + 1] < 245 || pixels[index + 2] < 245)
                {
                    count++;
                }
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
}
