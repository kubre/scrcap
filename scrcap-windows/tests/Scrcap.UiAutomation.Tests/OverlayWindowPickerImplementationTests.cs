using System.IO;
using System.Windows;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Overlay;

namespace Scrcap.UiAutomation.Tests;

public sealed class OverlayWindowPickerImplementationTests
{
    [Fact]
    public void OverlayIncludesWindowPickerInteractions()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("IWindowSelectionService", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateWindows", source, StringComparison.Ordinal);
        Assert.Contains("SelectWindowAsync", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCandidateForCommit", source, StringComparison.Ordinal);
        Assert.Contains("CycleWindowHighlight", source, StringComparison.Ordinal);
        Assert.Contains("Key.Tab", source, StringComparison.Ordinal);
        Assert.Contains("Key.Enter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectPointAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowPickerCyclesOnlyCandidatesOverlappingThePointerInZOrder()
    {
        WindowCandidate[] candidates =
        [
            new(new IntPtr(1), new PixelRect(0, 0, 300, 300), "top"),
            new(new IntPtr(2), new PixelRect(100, 100, 300, 300), "under"),
            new(new IntPtr(3), new PixelRect(500, 500, 100, 100), "elsewhere"),
        ];

        var stack = OverlayWindow.WindowCandidatesUnderPoint(candidates, new PixelPoint(150, 150));

        Assert.Collection(
            stack,
            candidate => Assert.Equal(new IntPtr(1), candidate.Hwnd),
            candidate => Assert.Equal(new IntPtr(2), candidate.Hwnd));
    }

    [Fact]
    public void WindowPickerCommitRefreshesTheExactSelectedHwnd()
    {
        var selected = new WindowCandidate(new IntPtr(2), new PixelRect(100, 100, 300, 300), "old");
        WindowCandidate[] refreshed =
        [
            new(new IntPtr(1), new PixelRect(0, 0, 300, 300), "top"),
            new(new IntPtr(2), new PixelRect(120, 130, 320, 310), "updated"),
        ];

        var committed = OverlayWindow.ResolveCandidateForCommit(selected, refreshed);

        Assert.NotNull(committed);
        Assert.Equal(new IntPtr(2), committed.Hwnd);
        Assert.Equal(new PixelRect(120, 130, 320, 310), committed.Bounds);
    }

    [Fact]
    public void WindowPickerCommitRejectsAStaleSelectedHwndInsteadOfFallingBackToTopmost()
    {
        var selected = new WindowCandidate(new IntPtr(2), new PixelRect(100, 100, 300, 300), "closed");
        WindowCandidate[] refreshed =
        [
            new(new IntPtr(1), new PixelRect(0, 0, 300, 300), "top"),
        ];

        Assert.Null(OverlayWindow.ResolveCandidateForCommit(selected, refreshed));
    }

    [Fact]
    public void OverlayIncludesPlanRequiredVisualStates()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml");
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("MonitorLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowHighlight", xaml, StringComparison.Ordinal);
        Assert.Contains("HintTag", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlapTag", xaml, StringComparison.Ordinal);
        Assert.Contains("CountdownOverlay", xaml, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard RepeatBehavior=\"Forever\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.Contains("StartSelectionAnimation", source, StringComparison.Ordinal);
        Assert.Contains("StopSelectionAnimation", source, StringComparison.Ordinal);
        Assert.Contains("RenderMonitorLayer", source, StringComparison.Ordinal);
        Assert.Contains("CountIntersectingMonitors", source, StringComparison.Ordinal);
        Assert.Contains("ShowCountdownAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRoutesDelayedCaptureCountdownThroughOverlay()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("settings.CaptureDelaySeconds", source, StringComparison.Ordinal);
        Assert.Contains("SelectRegionAsync(countdownSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("RequestFrom(settings, 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestFrom(settings, action == AppAction.CaptureDelayed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionSpaceMoveDoesNotJumpAndKeepsResizeAnchor()
    {
        var drag = new OverlaySelectionDragState();

        drag.Begin(new Point(10, 10));

        Assert.Equal(new Rect(10, 10, 100, 50), drag.ResizeTo(new Point(110, 60)));
        Assert.Equal(new Rect(10, 10, 100, 50), drag.MoveTo(new Point(110, 60)));
        Assert.Equal(new Rect(20, 20, 100, 50), drag.MoveTo(new Point(120, 70)));
        Assert.Equal(new Rect(20, 20, 110, 55), drag.ResizeTo(new Point(130, 75)));
    }

    [Fact]
    public void OverlayGeometryConvertsLogicalSelectionsToPhysicalPixelsAtCommonDpiScales()
    {
        foreach (var (scale, expected) in new[]
                 {
                     (1.0, new PixelRect(10, 20, 100, 50)),
                     (1.5, new PixelRect(15, 30, 150, 75)),
                     (2.0, new PixelRect(20, 40, 200, 100)),
                 })
        {
            OverlayMonitorBounds[] monitors =
            [
                new(1, new PixelRect(0, 0, (int)(1920 * scale), (int)(1080 * scale)), true, scale, scale),
            ];

            var actual = OverlayGeometry.ToCapturePixelRect(new Rect(10, 20, 100, 50), monitors, 0, 0);

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void OverlayGeometryFloorsAndCeilsFractionalDpiSelectionEdges()
    {
        OverlayMonitorBounds[] monitors =
        [
            new(1, new PixelRect(0, 0, 1920, 1080), true, 1.5, 1.5),
        ];

        var actual = OverlayGeometry.ToCapturePixelRect(new Rect(10.2, 20.2, 100.4, 50.4), monitors, 0, 0);

        Assert.Equal(new PixelRect(15, 30, 151, 76), actual);
    }

    [Fact]
    public void OverlayGeometryHandlesNegativeVirtualOriginsAndMixedDpiMonitors()
    {
        OverlayMonitorBounds[] monitors =
        [
            new(1, new PixelRect(-1280, 0, 1280, 720), false, 1, 1),
            new(2, new PixelRect(0, 0, 3000, 2000), true, 2, 2),
        ];
        var overlay = OverlayGeometry.VirtualOverlayBounds(monitors);

        Assert.Equal(-1280, overlay.Left);
        Assert.Equal(0, overlay.Top);
        Assert.Equal(2780, overlay.Width);
        Assert.Equal(1000, overlay.Height);

        var leftSelection = OverlayGeometry.ToCapturePixelRect(new Rect(40, 10, 100, 50), monitors, overlay.Left, overlay.Top);
        var primarySelection = OverlayGeometry.ToCapturePixelRect(new Rect(1320, 10, 100, 50), monitors, overlay.Left, overlay.Top);

        Assert.Equal(new PixelRect(-1240, 10, 100, 50), leftSelection);
        Assert.Equal(new PixelRect(80, 20, 200, 100), primarySelection);
    }

    [Fact]
    public void OverlayGeometryKeepsRightAndBelowMixedDpiDisplaysAdjacent()
    {
        OverlayMonitorBounds[] monitors =
        [
            new(1, new PixelRect(0, 0, 3840, 2160), true, 2, 2),
            new(2, new PixelRect(3840, 0, 1920, 1080), false, 1, 1),
            new(3, new PixelRect(0, 2160, 2560, 1440), false, 1, 1),
        ];
        var overlay = OverlayGeometry.VirtualOverlayBounds(monitors);

        Assert.Equal(new Rect(0, 0, 3840, 2520), overlay);
        Assert.Equal(new Rect(0, 0, 1920, 1080), OverlayGeometry.LogicalBoundsFor(monitors[0], monitors));
        Assert.Equal(new Rect(1920, 0, 1920, 1080), OverlayGeometry.LogicalBoundsFor(monitors[1], monitors));
        Assert.Equal(new Rect(0, 1080, 2560, 1440), OverlayGeometry.LogicalBoundsFor(monitors[2], monitors));

        var rightSelection = OverlayGeometry.ToCapturePixelRect(new Rect(1930, 10, 100, 50), monitors, overlay.Left, overlay.Top);
        var belowSelection = OverlayGeometry.ToCapturePixelRect(new Rect(10, 1090, 100, 50), monitors, overlay.Left, overlay.Top);

        Assert.Equal(new PixelRect(3850, 10, 100, 50), rightSelection);
        Assert.Equal(new PixelRect(10, 2170, 100, 50), belowSelection);
    }

    [Fact]
    public void ScrollingHudPlacementUsesSelectedMonitorLogicalBounds()
    {
        OverlayMonitorBounds[] monitors =
        [
            new(1, new PixelRect(-1280, 0, 1280, 720), false, 1, 1),
            new(2, new PixelRect(0, 0, 3000, 2000), true, 2, 2),
        ];

        var leftPlacement = ScrollingCaptureHud.PlacementFor(new PixelRect(-900, 100, 300, 200), monitors, 220, 64);
        var primaryPlacement = ScrollingCaptureHud.PlacementFor(new PixelRect(800, 200, 500, 300), monitors, 220, 64);

        Assert.Equal(-238, leftPlacement.X);
        Assert.Equal(18, leftPlacement.Y);
        Assert.Equal(1262, primaryPlacement.X);
        Assert.Equal(18, primaryPlacement.Y);
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
