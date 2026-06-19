using System.IO;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Overlay;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace Scrcap.UiAutomation.Tests;

public sealed class AppFlowImplementationTests
{
    [Fact]
    public void AppRoutesWindowCaptureThroughWindowPicker()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("WindowTargetAsync", source, StringComparison.Ordinal);
        Assert.Contains("SelectWindowAsync", source, StringComparison.Ordinal);
        Assert.Contains("candidate.Hwnd", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowFromPoint(point.Value)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureWindow => new CaptureTarget(AppAction.CaptureWindow, null)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppShowsCopyNotificationAfterSuccessfulClipboardCopy()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("Clipboard.SetImage", source, StringComparison.Ordinal);
        Assert.Contains("tray?.Notify(\"scrcap copied\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRoutesScrollingCaptureThroughScrollingService()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("CaptureScrollingRegionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ScrollingCaptureHud.ShowFor", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", source, StringComparison.Ordinal);
        Assert.Contains("BeforeScreenCapture: hud.HideForCaptureAsync", source, StringComparison.Ordinal);
        Assert.Contains("AfterScreenCapture: hud.ShowAfterCaptureAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureRegion or AppAction.CaptureDelayed or AppAction.CaptureScrolling", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppClosesSelectionOverlaysAfterUse()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("overlay.Close()", source, StringComparison.Ordinal);
        Assert.Contains("WaitForOverlayDismissalAsync", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHidesOwnWindowsAroundCapture()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("HideAppWindowsForCapture", source, StringComparison.Ordinal);
        Assert.Contains("window is not OverlayWindow and not ScrollingCaptureHud", source, StringComparison.Ordinal);
        Assert.Contains("window.Hide()", source, StringComparison.Ordinal);
        Assert.Contains("window.Show()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollingHudHideWaitsForRenderIdleBeforeCapture()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/ScrollingCaptureHud.xaml.cs");

        Assert.Contains("HideForCaptureAsync", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(50)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayUsesPunchedDimMaskAndLayeredHazardAnts()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml");
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("x:Name=\"DimMask\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionAntBlack", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionAntWhite", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionAntRed", xaml, StringComparison.Ordinal);
        Assert.Contains("FillRule = FillRule.EvenOdd", source, StringComparison.Ordinal);
        Assert.Contains("UpdateDimMask(rect)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayWindowPickerCommitsCandidateAndShowsOverlapIndex()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("SelectWindowAsync", source, StringComparison.Ordinal);
        Assert.Contains("windowCompletion.TrySetResult(candidate)", source, StringComparison.Ordinal);
        Assert.Contains("WindowStackAt", source, StringComparison.Ordinal);
        Assert.Contains("OverlapIndexText", source, StringComparison.Ordinal);
        Assert.Contains("CycleWindowHighlight", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayTagsUseWindowsKeycapsAndMultiplicationSign()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("AddKeycap(HintTagText, \"Space\")", source, StringComparison.Ordinal);
        Assert.Contains("AddKeycap(HintTagText, \"Esc\")", source, StringComparison.Ordinal);
        Assert.Contains("AddKeycap(OverlapText, \"Space\")", source, StringComparison.Ordinal);
        Assert.Contains("AddKeycap(OverlapText, \"Esc\")", source, StringComparison.Ordinal);
        Assert.Contains(" × ", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayTagClampStaysInsideCurrentMixedDpiMonitor()
    {
        var monitors = new[]
        {
            new OverlayMonitorBounds(1, new PixelRect(0, 0, 1920, 1080), true),
            new OverlayMonitorBounds(2, new PixelRect(1920, 0, 2560, 1440), false, 2, 2),
        };
        var secondaryBounds = OverlayGeometry.MonitorOverlayBoundsFor(new WpfPoint(2000, 100), monitors, 0, 0);
        var clamped = OverlayGeometry.ClampTagPosition(new WpfPoint(3180, -20), new WpfSize(200, 80), secondaryBounds);

        Assert.Equal(new WpfRect(1920, 0, 1280, 720), secondaryBounds);
        Assert.Equal(2992, clamped.X);
        Assert.Equal(8, clamped.Y);
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
