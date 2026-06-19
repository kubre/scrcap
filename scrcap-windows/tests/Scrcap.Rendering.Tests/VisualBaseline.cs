using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Scrcap.Rendering.Tests;

internal static class VisualBaseline
{
    private const string AcceptEnvironmentVariable = "SCRCAP_ACCEPT_RENDERING_BASELINES";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void AssertMatches(string baselineName, BitmapSource actual, int tolerance = 0)
    {
        var projectDirectory = FindProjectDirectory();
        var baselineDirectory = Path.Combine(projectDirectory, "Baselines");
        var expectedPath = Path.Combine(baselineDirectory, baselineName + ".png");
        var actualBytes = EncodePng(actual);

        if (AcceptBaselines())
        {
            Directory.CreateDirectory(baselineDirectory);
            File.WriteAllBytes(expectedPath, actualBytes);
            return;
        }

        if (!File.Exists(expectedPath))
        {
            WriteDiffArtifacts(projectDirectory, baselineName, actualBytes, null, MissingBaselineSummary(baselineName));
            throw new Xunit.Sdk.XunitException(
                $"Missing visual baseline '{expectedPath}'. Set {AcceptEnvironmentVariable}=1 to create it intentionally.");
        }

        var expectedBytes = File.ReadAllBytes(expectedPath);
        var expected = DecodeBgra(expectedBytes);
        var observed = DecodeBgra(actualBytes);
        var summary = Compare(baselineName, expected, observed, tolerance);
        if (summary.Matches)
        {
            return;
        }

        WriteDiffArtifacts(projectDirectory, baselineName, actualBytes, expectedBytes, summary);
        throw new Xunit.Sdk.XunitException(
            $"Visual baseline '{baselineName}' differed: {summary.DifferentPixels} pixels changed, max channel delta {summary.MaxChannelDelta}. Diff artifacts were written to {DiffDirectory(projectDirectory, baselineName)}.");
    }

    public static BitmapSource RenderElement(FrameworkElement element, int width, int height)
    {
        element.Width = width;
        element.Height = height;
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    public static BitmapSource RenderWindow(Window window, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.Show();
        DrainDispatcher();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        bitmap.Freeze();
        window.Close();
        DrainDispatcher();
        return bitmap;
    }

    private static void WriteDiffArtifacts(string projectDirectory, string baselineName, byte[] actualBytes, byte[]? expectedBytes, VisualDiffSummary summary)
    {
        var diffDirectory = DiffDirectory(projectDirectory, baselineName);
        Directory.CreateDirectory(diffDirectory);
        File.WriteAllBytes(Path.Combine(diffDirectory, "actual.png"), actualBytes);
        if (expectedBytes is not null)
        {
            File.WriteAllBytes(Path.Combine(diffDirectory, "expected.png"), expectedBytes);
            File.WriteAllBytes(Path.Combine(diffDirectory, "heatmap.png"), EncodePng(CreateHeatmap(DecodeBgra(expectedBytes), DecodeBgra(actualBytes))));
        }

        File.WriteAllText(Path.Combine(diffDirectory, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
    }

    private static VisualDiffSummary Compare(string baselineName, PixelBuffer expected, PixelBuffer actual, int tolerance)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new VisualDiffSummary(
                baselineName,
                false,
                expected.Width,
                expected.Height,
                actual.Width,
                actual.Height,
                expected.Width * expected.Height,
                int.MaxValue,
                1.0,
                "Image dimensions differ.");
        }

        var differentPixels = 0;
        var maxChannelDelta = 0;
        for (var index = 0; index < expected.Pixels.Length; index += 4)
        {
            var pixelDifferent = false;
            for (var channel = 0; channel < 4; channel++)
            {
                var delta = Math.Abs(expected.Pixels[index + channel] - actual.Pixels[index + channel]);
                maxChannelDelta = Math.Max(maxChannelDelta, delta);
                pixelDifferent |= delta > tolerance;
            }

            if (pixelDifferent)
            {
                differentPixels++;
            }
        }

        var totalPixels = expected.Width * expected.Height;
        return new VisualDiffSummary(
            baselineName,
            differentPixels == 0,
            expected.Width,
            expected.Height,
            actual.Width,
            actual.Height,
            differentPixels,
            maxChannelDelta,
            totalPixels == 0 ? 0 : (double)differentPixels / totalPixels,
            null);
    }

    private static BitmapSource CreateHeatmap(PixelBuffer expected, PixelBuffer actual)
    {
        var width = Math.Min(expected.Width, actual.Width);
        var height = Math.Min(expected.Height, actual.Height);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sourceOffset = ((y * expected.Width) + x) * 4;
                var actualOffset = ((y * actual.Width) + x) * 4;
                var targetOffset = ((y * width) + x) * 4;
                var delta = 0;
                for (var channel = 0; channel < 4; channel++)
                {
                    delta = Math.Max(delta, Math.Abs(expected.Pixels[sourceOffset + channel] - actual.Pixels[actualOffset + channel]));
                }

                pixels[targetOffset] = (byte)(255 - delta);
                pixels[targetOffset + 1] = (byte)(255 - delta);
                pixels[targetOffset + 2] = 255;
                pixels[targetOffset + 3] = 255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static VisualDiffSummary MissingBaselineSummary(string baselineName) =>
        new(baselineName, false, 0, 0, 0, 0, 0, 0, 0, "Missing expected baseline.");

    private static PixelBuffer DecodeBgra(byte[] pngBytes)
    {
        using var stream = new MemoryStream(pngBytes);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return new PixelBuffer(converted.PixelWidth, converted.PixelHeight, pixels);
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string DiffDirectory(string projectDirectory, string baselineName) =>
        Path.Combine(projectDirectory, "TestResults", "VisualDiffs", baselineName);

    private static bool AcceptBaselines()
    {
        var value = Environment.GetEnvironmentVariable(AcceptEnvironmentVariable);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Scrcap.Rendering.Tests.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Scrcap.Rendering.Tests project directory.");
    }

    private static void DrainDispatcher()
    {
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private sealed record PixelBuffer(int Width, int Height, byte[] Pixels);

    private sealed record VisualDiffSummary(
        string BaselineName,
        bool Matches,
        int ExpectedWidth,
        int ExpectedHeight,
        int ActualWidth,
        int ActualHeight,
        int DifferentPixels,
        int MaxChannelDelta,
        double DifferenceRatio,
        string? FailureReason);
}
