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
        Assert.DoesNotContain("BitBlt", service, StringComparison.Ordinal);

        Assert.Contains("Direct3D11CaptureFramePool.CreateFreeThreaded", wgc, StringComparison.Ordinal);
        Assert.Contains("GraphicsCaptureSession.IsSupported", wgc, StringComparison.Ordinal);
        Assert.Contains("SoftwareBitmap.CreateCopyFromSurfaceAsync", wgc, StringComparison.Ordinal);
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

        Assert.Contains("CaptureRegionAsync(PixelRect rect", models, StringComparison.Ordinal);
        Assert.Contains("CaptureWindowAsync(IntPtr hwnd", models, StringComparison.Ordinal);
        Assert.Contains("CaptureMonitorUnderCursorAsync", models, StringComparison.Ordinal);
        Assert.Contains("CaptureScrollingRegionAsync", models, StringComparison.Ordinal);
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
        Assert.Contains("WaitForScrollSettleAsync", service, StringComparison.Ordinal);
        Assert.Contains("bottomProbeCount", service, StringComparison.Ordinal);
        Assert.Contains("ScrollingFramePlanner.DetectStickyHeaderRows", service, StringComparison.Ordinal);
        Assert.Contains("ScrollingFramePlanner.FindNewContentStart", service, StringComparison.Ordinal);
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
