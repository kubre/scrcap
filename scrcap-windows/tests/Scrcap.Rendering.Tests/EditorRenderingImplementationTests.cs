using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Core;
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
