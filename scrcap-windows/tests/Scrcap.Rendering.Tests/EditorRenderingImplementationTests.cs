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
    public void FlattenPngUsesLogicalDocumentSizeAndClampsExportToNativePixels()
    {
        RunSta(() =>
        {
            var settings = Settings.Defaults();
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(100, 50);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(200, 100, Colors.White),
                Width = 100,
                Height = 50,
            };
            canvas.SetSourcePixelsPerDip(2, 2);

            var native = DecodeFrame(canvas.FlattenPng(scale: 2));
            var logical = DecodeFrame(canvas.FlattenPng(scale: 1));

            Assert.Equal(200, native.PixelWidth);
            Assert.Equal(100, native.PixelHeight);
            Assert.InRange(native.DpiX, 191.9, 192.1);
            Assert.InRange(native.DpiY, 191.9, 192.1);
            Assert.Equal(100, logical.PixelWidth);
            Assert.Equal(50, logical.PixelHeight);
            Assert.InRange(logical.DpiX, 95.9, 96.1);
            Assert.InRange(logical.DpiY, 95.9, 96.1);
        });
    }

    [Fact]
    public void FlattenPngDoesNotUpscaleOneXCaptures()
    {
        RunSta(() =>
        {
            var viewModel = new EditorViewModel(Settings.Defaults());
            viewModel.LoadDocument(80, 60);
            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(80, 60, Colors.White),
            };
            canvas.SetSourcePixelsPerDip(1, 1);

            var frame = DecodeFrame(canvas.FlattenPng(scale: 2));

            Assert.Equal(80, frame.PixelWidth);
            Assert.Equal(60, frame.PixelHeight);
            Assert.InRange(frame.DpiX, 95.9, 96.1);
        });
    }

    [Fact]
    public void RasterGeometryKeepsCropAndExpansionPixelAlignedAtFractionalDpi()
    {
        var crop = EditorRasterGeometry.AlignCrop(new CoreRect(10.1, 5.2, 100.2, 40.4), 300, 180, 1.5, 1.5);
        var expansion = EditorRasterGeometry.AlignExpansion(new CanvasExpansion(2.2, 3.1, 4.2, 5.1), 1.5, 1.5);

        Assert.Equal(new Int32Rect(15, 7, 151, 62), crop.Pixels);
        Assert.Equal(crop.Pixels.Width, (int)Math.Round(crop.Logical.Width * 1.5));
        Assert.Equal(crop.Pixels.Height, (int)Math.Round(crop.Logical.Height * 1.5));
        Assert.Equal(3, expansion.LeftPixels);
        Assert.Equal(5, expansion.TopPixels);
        Assert.Equal(6, expansion.RightPixels);
        Assert.Equal(8, expansion.BottomPixels);
        Assert.Equal(expansion.LeftPixels, (int)Math.Round(expansion.Logical.Left * 1.5));
    }

    [Fact]
    public void ClipboardPayloadContainsBitmapCompatibilityAndExactPngBytes()
    {
        RunSta(() =>
        {
            var png = EncodePng(SolidBitmap(12, 8, Colors.CornflowerBlue));
            var data = EditorClipboard.CreateDataObject(png);

            Assert.True(data.GetDataPresent(DataFormats.Bitmap));
            Assert.True(data.GetDataPresent(EditorClipboard.PngFormat, autoConvert: false));
            using var stream = Assert.IsType<MemoryStream>(data.GetData(EditorClipboard.PngFormat, autoConvert: false));
            Assert.Equal(png, stream.ToArray());
        });
    }

    [Fact]
    public void DragOutPayloadCreatesPngFileDropAndCanCleanItUp()
    {
        RunSta(() =>
        {
            var png = EncodePng(SolidBitmap(10, 6, Colors.Orange));
            var path = DragOutPayload.CreateTempPng(png, "scrcap-{date}-{time}.png", new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
            try
            {
                var data = DragOutPayload.CreateDataObject(path);
                var files = Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop));

                Assert.Single(files);
                Assert.Equal(Path.GetFullPath(path), files[0]);
                Assert.Equal(png, File.ReadAllBytes(path));
            }
            finally
            {
                DragOutPayload.Delete(path);
            }

            Assert.False(File.Exists(path));
        });
    }

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
    public void CroppedCounterAndTextAreClippedToTheVisibleImage()
    {
        WpfTestHost.Run(() =>
        {
            WpfTestHost.ApplyTheme(ThemeMode.Light);
            var settings = Settings.Defaults();
            settings.PaletteHex = ["#111111", "#00AA00", "#0066CC", "#FF0000", "#FF00FF"];
            var viewModel = new EditorViewModel(settings);
            viewModel.LoadDocument(100, 80);
            viewModel.ActiveTool = EditorTool.Counter;
            viewModel.CommitShape(new CorePoint(95, 40), new CorePoint(95, 40));
            viewModel.ActiveTool = EditorTool.Text;
            viewModel.PendingText = "CROPPED TEXT";
            viewModel.CommitShape(new CorePoint(90, 60), new CorePoint(90, 60));
            Assert.True(viewModel.Crop(new CoreRect(50, 20, 50, 40)));

            var canvas = new EditorCanvas
            {
                ViewModel = viewModel,
                SourceBitmap = SolidBitmap(50, 40, Colors.White),
            };
            var rendered = VisualBaseline.RenderElement(canvas, 200, 160);
            var pixels = CopyBgra(rendered);

            // The cropped image occupies x=75..125 and y=60..100. Counter/text
            // glyphs must not paint into the canvas area immediately to its right.
            for (var y = 50; y < 110; y++)
            {
                for (var x = 127; x < 155; x++)
                {
                    var offset = ((y * rendered.PixelWidth) + x) * 4;
                    Assert.InRange(pixels[offset], (byte)217, (byte)219);
                    Assert.InRange(pixels[offset + 1], (byte)214, (byte)216);
                    Assert.InRange(pixels[offset + 2], (byte)213, (byte)215);
                }
            }
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

    private static BitmapFrame DecodeFrame(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        return new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
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
