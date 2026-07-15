using System.Drawing;
using System.Drawing.Imaging;
using Scrcap.Windows.Platform.Capture;

namespace Scrcap.Capture.Tests;

public sealed class CaptureImplementationTests
{
    [Fact]
    public void CaptureServiceUsesWindowsGraphicsCaptureBeforeFallback()
    {
        var service = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/WindowsCaptureService.cs");
        var wgc = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/WindowsGraphicsCaptureBackend.cs");
        var fallback = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/GdiCaptureFallback.cs");

        Assert.Contains("WindowsGraphicsCaptureBackend", service, StringComparison.Ordinal);
        Assert.Contains("GdiCaptureFallback", service, StringComparison.Ordinal);
        Assert.Contains("graphicsCapture.CaptureWindowAsync", service, StringComparison.Ordinal);
        Assert.Contains("graphicsCapture.CaptureMonitorAsync", service, StringComparison.Ordinal);
        Assert.Contains("WindowsGraphicsCaptureBackend.CanFallback", service, StringComparison.Ordinal);
        Assert.Contains("CaptureBackendPreference.WindowsGraphicsCapture", service, StringComparison.Ordinal);
        Assert.Contains("FallbackReason(exception)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("BitBlt", service, StringComparison.Ordinal);

        Assert.Contains("Direct3D11CaptureFramePool.CreateFreeThreaded", wgc, StringComparison.Ordinal);
        Assert.Contains("GraphicsCaptureSession.IsSupported", wgc, StringComparison.Ordinal);
        Assert.Contains("SoftwareBitmap.CreateCopyFromSurfaceAsync", wgc, StringComparison.Ordinal);
        Assert.Contains("IFrameConverter", wgc, StringComparison.Ordinal);
        Assert.Contains("CreateForWindow", wgc, StringComparison.Ordinal);
        Assert.Contains("CreateForMonitor", wgc, StringComparison.Ordinal);
        Assert.Contains("IsCursorCaptureEnabled", wgc, StringComparison.Ordinal);

        Assert.Contains("BitBlt", fallback, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureContractsExposeWindowMonitorRegionAndScrollingCapture()
    {
        var models = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/CaptureModels.cs");
        var service = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/WindowsCaptureService.cs");

        Assert.Contains("CapturedPixels", models, StringComparison.Ordinal);
        Assert.Contains("CaptureBackendPreference", models, StringComparison.Ordinal);
        Assert.Contains("CaptureBackendUsed", models, StringComparison.Ordinal);
        Assert.Contains("Unknown", models, StringComparison.Ordinal);
        Assert.Contains("Validate()", models, StringComparison.Ordinal);
        Assert.Contains("FallbackReason", models, StringComparison.Ordinal);
        Assert.DoesNotContain("PngBytes", models, StringComparison.Ordinal);
        Assert.Contains("CaptureRegionAsync(PixelRect rect", models, StringComparison.Ordinal);
        Assert.Contains("CaptureWindowAsync(IntPtr hwnd", models, StringComparison.Ordinal);
        Assert.Contains("CaptureMonitorUnderCursorAsync", models, StringComparison.Ordinal);
        Assert.Contains("CaptureScrollingRegionAsync", models, StringComparison.Ordinal);
        Assert.Contains("CreateResult(capture.Bitmap", service, StringComparison.Ordinal);
        Assert.Contains("CaptureRegionBitmapAsync", service, StringComparison.Ordinal);
        Assert.Contains("MonitorSelection.FromRect", service, StringComparison.Ordinal);
        Assert.Contains("MonitorSelection.FromPoint", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollingPlannerSkipsRepeatedStickyHeaderBeforeAligningNewRows()
    {
        ulong[] firstFrame = [100, 101, 1, 2, 3, 4, 5, 6];
        ulong[] accumulated = [100, 101, 1, 2, 3, 4, 5, 6];
        ulong[] nextFrame = [100, 101, 5, 6, 7, 8, 9, 10];

        var stickyRows = ScrollingFramePlanner.DetectStickyHeaderRows(firstFrame, nextFrame, 4);
        var newContentStart = ScrollingFramePlanner.FindNewContentStart(accumulated, nextFrame, stickyRows);

        Assert.Equal(2, stickyRows);
        Assert.Equal(4, newContentStart);
    }

    [Fact]
    public void ScrollingPlannerTreatsFullyRepeatedFrameAsBottom()
    {
        ulong[] accumulated = [1, 2, 3, 4, 5, 6];
        ulong[] repeated = [1, 2, 3, 4, 5, 6];

        var newContentStart = ScrollingFramePlanner.FindNewContentStart(accumulated, repeated, 0);
        var equal = ScrollingFramePlanner.RowsEqual(repeated, repeated);

        Assert.Equal(repeated.Length, newContentStart);
        Assert.True(equal);
    }

    [Fact]
    public void ScrollingCaptureHasSettleAndBottomProbeControls()
    {
        var models = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/CaptureModels.cs");
        var service = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/WindowsCaptureService.cs");

        Assert.Contains("StickyHeaderMaxPixels", models, StringComparison.Ordinal);
        Assert.Contains("BottomProbeFrames", models, StringComparison.Ordinal);
        Assert.Contains("RequiredStableSettleSamples", models, StringComparison.Ordinal);
        Assert.Contains("ScrollingCaptureStopReason", models, StringComparison.Ordinal);
        Assert.Contains("MemoryCapReached", models, StringComparison.Ordinal);
        Assert.Contains("EstimatedBytes", models, StringComparison.Ordinal);
        Assert.Contains("WaitForScrollSettleAsync", service, StringComparison.Ordinal);
        Assert.Contains("bottomProbeCount", service, StringComparison.Ordinal);
        Assert.Contains("CanRetainExtraRows", service, StringComparison.Ordinal);
        Assert.Contains("ReportProgress", service, StringComparison.Ordinal);
        Assert.Contains("ScrollingFramePlanner.DetectStickyHeaderRows", service, StringComparison.Ordinal);
        Assert.Contains("ScrollingFramePlanner.FindNewContentStart", service, StringComparison.Ordinal);
    }

    [Fact]
    public void LogicalSelectionConvertsToPhysicalPixelsForPerMonitorDpi()
    {
        var monitor = new DpiAwareMonitor(
            "DISPLAY1",
            new LogicalRect(0, 0, 1280, 720),
            new PixelRect(0, 0, 1920, 1080),
            1.5,
            1.5);

        var physical = CaptureGeometry.LogicalToPhysical(new LogicalRect(10.2, 20.2, 100.4, 50.4), monitor);

        Assert.Equal(new PixelRect(15, 30, 151, 76), physical);
    }

    [Fact]
    public void MixedDpiSelectionSplitsAtMonitorBoundaries()
    {
        DpiAwareMonitor[] monitors =
        [
            new("LEFT", new LogicalRect(0, 0, 1000, 800), new PixelRect(0, 0, 1000, 800), 1, 1),
            new("RIGHT", new LogicalRect(1000, 0, 800, 600), new PixelRect(1000, 0, 1200, 900), 1.5, 1.5),
        ];

        var segments = CaptureGeometry.LogicalToPhysicalSelections(new LogicalRect(950, 10, 100, 40), monitors);

        Assert.Collection(
            segments,
            left =>
            {
                Assert.Equal("LEFT", left.DeviceName);
                Assert.Equal(new PixelRect(950, 10, 50, 40), left.PhysicalPixels);
            },
            right =>
            {
                Assert.Equal("RIGHT", right.DeviceName);
                Assert.Equal(new PixelRect(1000, 15, 75, 60), right.PhysicalPixels);
            });
    }

    [Fact]
    public void MonitorUnderCursorPrefersContainingMonitorAndFallsBackToNearest()
    {
        DpiAwareMonitor[] monitors =
        [
            new("PRIMARY", new LogicalRect(0, 0, 1000, 800), new PixelRect(0, 0, 1000, 800), 1, 1),
            new("SECONDARY", new LogicalRect(1000, 0, 800, 600), new PixelRect(1000, 0, 1200, 900), 1.5, 1.5),
        ];

        Assert.Equal("SECONDARY", CaptureGeometry.MonitorUnderPoint(new LogicalPoint(1200, 100), monitors).DeviceName);
        Assert.Equal("PRIMARY", CaptureGeometry.MonitorUnderPoint(new LogicalPoint(-10, 300), monitors).DeviceName);
    }

    [Fact]
    public void WindowCompositionPreservesColoredCornersAndDoesNotIntroduceOverlayPixels()
    {
        using var source = CreateCornerBitmap(4, 4, Color.Cyan);
        var request = CaptureRequest(transparent: true, shadow: false, background: "#FF00FF");

        using var composed = WindowBitmapComposer.Compose(source, request);

        Assert.Equal(4, composed.Width);
        Assert.Equal(4, composed.Height);
        Assert.Equal(Color.Red.ToArgb(), composed.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), composed.GetPixel(3, 0).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), composed.GetPixel(0, 3).ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), composed.GetPixel(3, 3).ToArgb());
        Assert.DoesNotContain(ReadPixels(composed), color => color.ToArgb() == Color.Magenta.ToArgb());
    }

    [Fact]
    public void WindowCompositionAppliesBackgroundAndShadowPaddingVariants()
    {
        using var transparent = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        var backgroundRequest = CaptureRequest(transparent: false, shadow: false, background: "#112233");

        using var background = WindowBitmapComposer.Compose(transparent, backgroundRequest);

        Assert.Equal(Color.FromArgb(255, 17, 34, 51).ToArgb(), background.GetPixel(0, 0).ToArgb());

        using var opaque = CreateSolidBitmap(2, 2, Color.White);
        var shadowRequest = CaptureRequest(transparent: false, shadow: true, background: "#445566");

        using var shadow = WindowBitmapComposer.Compose(opaque, shadowRequest);

        Assert.Equal(2 + WindowBitmapComposer.ShadowPadding * 2, shadow.Width);
        Assert.Equal(2 + WindowBitmapComposer.ShadowPadding * 2, shadow.Height);
        Assert.Equal(Color.White.ToArgb(), shadow.GetPixel(WindowBitmapComposer.ShadowPadding, WindowBitmapComposer.ShadowPadding).ToArgb());
        Assert.Equal(Color.FromArgb(255, 68, 85, 102).ToArgb(), shadow.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void TransparentWindowShadowStaysTransparentAndIsSoftAndSymmetric()
    {
        using var source = CreateSolidBitmap(20, 12, Color.White);
        var request = CaptureRequest(transparent: true, shadow: true, background: "#FF00FF");

        using var composed = WindowBitmapComposer.Compose(source, request);

        var pad = WindowBitmapComposer.ShadowPadding;
        Assert.Equal(0, composed.GetPixel(0, 0).A);
        Assert.InRange(composed.GetPixel(pad - 1, pad + 6).A, 30, 80);
        Assert.True(composed.GetPixel(pad - 4, pad + 6).A > composed.GetPixel(pad - 12, pad + 6).A);
        Assert.Equal(composed.GetPixel(pad - 4, pad + 6).A, composed.GetPixel(pad + 20 + 3, pad + 6).A);
        Assert.Equal(Color.White.ToArgb(), composed.GetPixel(pad, pad).ToArgb());
        Assert.DoesNotContain(ReadPixels(composed), color => color.R == 255 && color.B == 255 && color.G == 0);
    }

    [Fact]
    public void ScrollingStitcherBuildsExpectedBitmapForNormalStickyAndLazyFrames()
    {
        using var first = CreateRows([100, 0, 1, 2, 3, 4]);
        using var lazy = CreateRows([100, 0, 1, 2, 3, 4]);
        using var second = CreateRows([100, 3, 4, 5, 6, 7]);
        using var third = CreateRows([100, 6, 7, 8, 9, 10]);

        using var result = ScrollingCaptureStitcher.Stitch(
            [first, lazy, second, third],
            new ScrollingStitchOptions(MaxHeight: 20, StickyHeaderMaxPixels: 1, BottomProbeFrames: 4));

        Assert.Equal(ScrollingCaptureStopReason.Completed, result.StopReason);
        Assert.Equal([100, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], ReadRowIds(result.Bitmap));
    }

    [Fact]
    public void ScrollingStitcherDetectsNoScrollBounceAndMaxHeight()
    {
        using var first = CreateRows([0, 1, 2, 3, 4, 5]);
        using var repeatedOnce = CreateRows([0, 1, 2, 3, 4, 5]);
        using var repeatedTwice = CreateRows([0, 1, 2, 3, 4, 5]);

        using var oneProbe = ScrollingCaptureStitcher.Stitch(
            [first, repeatedOnce],
            new ScrollingStitchOptions(MaxHeight: 20, BottomProbeFrames: 2));

        Assert.Equal(ScrollingCaptureStopReason.Completed, oneProbe.StopReason);
        Assert.Equal([0, 1, 2, 3, 4, 5], ReadRowIds(oneProbe.Bitmap));

        using var noScroll = ScrollingCaptureStitcher.Stitch(
            [first, repeatedOnce, repeatedTwice],
            new ScrollingStitchOptions(MaxHeight: 20, BottomProbeFrames: 2));

        Assert.Equal(ScrollingCaptureStopReason.BottomReached, noScroll.StopReason);
        Assert.Equal([0, 1, 2, 3, 4, 5], ReadRowIds(noScroll.Bitmap));

        using var next = CreateRows([4, 5, 6, 7, 8, 9]);
        using var capped = ScrollingCaptureStitcher.Stitch(
            [first, next],
            new ScrollingStitchOptions(MaxHeight: 8, BottomProbeFrames: 4));

        Assert.Equal(ScrollingCaptureStopReason.MaxHeightReached, capped.StopReason);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], ReadRowIds(capped.Bitmap));
    }

    [Fact]
    public void ScrollingCaptureSourceKeepsFailureCleanupAndExpectedStops()
    {
        var service = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/WindowsCaptureService.cs");
        var stitcher = ReadRepoFile("src/Scrcap.Windows.Platform/Capture/ScrollingCaptureStitcher.cs");

        Assert.Contains("finally", service, StringComparison.Ordinal);
        Assert.Contains("frame.Dispose()", service, StringComparison.Ordinal);
        Assert.Contains("CaptureFailed", service, StringComparison.Ordinal);
        Assert.Contains("Cancelled", service, StringComparison.Ordinal);
        Assert.Contains("Timeout", service, StringComparison.Ordinal);
        Assert.Contains("AlignmentFailed", service, StringComparison.Ordinal);
        Assert.Contains("MaxHeightReached", stitcher, StringComparison.Ordinal);
        Assert.Contains("BottomReached", stitcher, StringComparison.Ordinal);
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

    private static CaptureRequest CaptureRequest(bool transparent, bool shadow, string background) =>
        new(
            IncludeCursor: false,
            IncludeWindowShadow: shadow,
            WindowBackgroundTransparent: transparent,
            WindowBackgroundHex: background,
            DelaySeconds: 0);

    private static Bitmap CreateCornerBitmap(int width, int height, Color fill)
    {
        var bitmap = CreateSolidBitmap(width, height, fill);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(width - 1, 0, Color.Lime);
        bitmap.SetPixel(0, height - 1, Color.Blue);
        bitmap.SetPixel(width - 1, height - 1, Color.Yellow);
        return bitmap;
    }

    private static Bitmap CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static Bitmap CreateRows(int[] rowIds)
    {
        var bitmap = new Bitmap(3, rowIds.Length, PixelFormat.Format32bppArgb);
        for (var y = 0; y < rowIds.Length; y++)
        {
            using var brush = new SolidBrush(RowColor(rowIds[y]));
            using var graphics = Graphics.FromImage(bitmap);
            graphics.FillRectangle(brush, 0, y, bitmap.Width, 1);
        }

        return bitmap;
    }

    private static IReadOnlyList<int> ReadRowIds(Bitmap bitmap)
    {
        var rows = new int[bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            rows[y] = bitmap.GetPixel(0, y).R;
        }

        return rows;
    }

    private static IReadOnlyList<Color> ReadPixels(Bitmap bitmap)
    {
        var pixels = new List<Color>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                pixels.Add(bitmap.GetPixel(x, y));
            }
        }

        return pixels;
    }

    private static Color RowColor(int rowId) => Color.FromArgb(255, rowId, rowId, rowId);
}
