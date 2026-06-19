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
            viewModel.PendingText = "QA";
            viewModel.CommitShape(new CorePoint(10, 10), new CorePoint(10, 10));

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
                10, 20, 30, 40,
                50, 60, 70, 80,
                90, 100, 110, 120,
                130, 140, 150, 160,
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
