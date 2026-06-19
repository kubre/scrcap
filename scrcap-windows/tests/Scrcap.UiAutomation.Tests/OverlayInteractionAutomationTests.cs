using System.Windows;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.UI.Overlay;

namespace Scrcap.UiAutomation.Tests;

public sealed class OverlayInteractionAutomationTests
{
    [Fact]
    public void RegionDragSpaceMoveAndEscapeCancelUseRuntimeOverlayState()
    {
        RunSta(() =>
        {
            var overlay = NewOverlay();

            overlay.BeginRegionDragForTests(new Point(20, 30));
            Assert.True(overlay.IsSelectionAnimationEnabledForTests);
            overlay.MoveRegionDragForTests(new Point(180, 130), moveSelection: false);
            Assert.Equal(new Rect(20, 30, 160, 100), overlay.CurrentSelectionForTests);

            overlay.MoveRegionDragForTests(new Point(180, 130), moveSelection: true);
            Assert.Equal(new Rect(20, 30, 160, 100), overlay.CurrentSelectionForTests);
            overlay.MoveRegionDragForTests(new Point(220, 160), moveSelection: true);
            Assert.Equal(new Rect(60, 60, 160, 100), overlay.CurrentSelectionForTests);

            var rect = overlay.CompleteRegionDragForTests();
            Assert.Equal(new PixelRect(60, 60, 160, 100), rect);
            Assert.False(overlay.IsSelectionAnimationEnabledForTests);

            overlay.BeginRegionDragForTests(new Point(5, 5));
            overlay.MoveRegionDragForTests(new Point(50, 50), moveSelection: false);
            overlay.CancelForTests();
            Assert.Equal(Rect.Empty, overlay.CurrentSelectionForTests);
            Assert.False(overlay.IsSelectionAnimationEnabledForTests);
            overlay.Close();
        });
    }

    [Fact]
    public void WindowHoverClickAndTabCycleUseFixtureWindowStack()
    {
        RunSta(() =>
        {
            var top = new WindowCandidate(new IntPtr(10), new PixelRect(20, 20, 120, 100), "Top");
            var middle = new WindowCandidate(new IntPtr(20), new PixelRect(10, 10, 130, 120), "Middle");
            var fixture = new FakeWindowSelectionService([top, middle], top);
            var overlay = NewOverlay(fixture);

            _ = overlay.SelectWindowAsync();
            overlay.HighlightWindowAtForTests(new Point(40, 40));
            Assert.Equal(top, overlay.HighlightedWindowForTests);
            Assert.Contains("1/2", overlay.WindowLabelTextForTests, StringComparison.Ordinal);

            overlay.CycleWindowHighlightForTests(1);
            Assert.Equal(middle, overlay.HighlightedWindowForTests);
            Assert.Contains("2/2", overlay.WindowLabelTextForTests, StringComparison.Ordinal);

            overlay.CommitPointOrWindowForTests(new Point(40, 40));
            Assert.True(fixture.Enumerated);
            overlay.Close();
        });
    }

    private static OverlayWindow NewOverlay(IWindowSelectionService? service = null) =>
        new(
            service ?? new FakeWindowSelectionService([], null),
            [new OverlayMonitorBounds(1, new PixelRect(0, 0, 400, 300), true, 1, 1)])
        {
            Width = 400,
            Height = 300,
        };

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

    private sealed class FakeWindowSelectionService(
        IReadOnlyList<WindowCandidate> candidates,
        WindowCandidate? pointCandidate) : IWindowSelectionService
    {
        public bool Enumerated { get; private set; }

        public WindowCandidate? WindowFromPoint(PixelPoint point) => pointCandidate;

        public IReadOnlyList<WindowCandidate> EnumerateWindows()
        {
            Enumerated = true;
            return candidates;
        }
    }
}
