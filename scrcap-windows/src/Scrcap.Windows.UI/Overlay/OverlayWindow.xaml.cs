using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using Scrcap.Windows.Platform.Capture;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize = System.Windows.Size;

namespace Scrcap.Windows.UI.Overlay;

public partial class OverlayWindow : Window
{
    private readonly IWindowSelectionService windowSelectionService;
    private readonly IReadOnlyList<OverlayMonitorBounds> monitorBounds;
    private readonly DispatcherTimer selectionAntTimer;
    private readonly TaskCompletionSource<PixelRect?> rectCompletion = new();
    private readonly TaskCompletionSource<WindowCandidate?> windowCompletion = new();
    private readonly OverlaySelectionDragState selectionDrag = new();
    private IReadOnlyList<WindowCandidate> windowCandidates = [];
    private IReadOnlyList<WindowCandidate> overlappingWindowCandidates = [];
    private WindowCandidate? highlightedWindow;
    private PixelPoint? windowPointer;
    private bool isMovingSelection;
    private bool isDraggingSelection;
    private Rect currentSelection;
    private bool pointMode;
    private bool isCompleting;
    private bool isCancelled;
    private int countdownSeconds;

    public OverlayWindow()
        : this(new WindowSelectionService(), OverlayGeometry.CreateMonitorBounds())
    {
    }

    internal OverlayWindow(IWindowSelectionService windowSelectionService, IReadOnlyList<OverlayMonitorBounds> monitorBounds)
    {
        this.windowSelectionService = windowSelectionService;
        this.monitorBounds = monitorBounds;
        InitializeComponent();
        selectionAntTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Render, (_, _) =>
        {
            Selection.StrokeDashOffset = (Selection.StrokeDashOffset + 1) % 20;
        }, Dispatcher);
        selectionAntTimer.Stop();
        var overlayBounds = OverlayGeometry.VirtualOverlayBounds(monitorBounds);
        Left = overlayBounds.Left;
        Top = overlayBounds.Top;
        Width = overlayBounds.Width;
        Height = overlayBounds.Height;
        Cursor = System.Windows.Input.Cursors.Cross;
        Loaded += (_, _) =>
        {
            RenderMonitorLayer();
            Focus();
        };
    }

    public Task<PixelRect?> SelectRegionAsync(int delayedCountdownSeconds = 0)
    {
        pointMode = false;
        countdownSeconds = Math.Max(0, delayedCountdownSeconds);
        HintTag.Text = countdownSeconds > 0
            ? $"Drag to choose a region. A {countdownSeconds}s countdown starts before capture."
            : "Drag to capture a region. Press Esc to cancel.";
        HintTag.Visibility = Visibility.Visible;
        Show();
        Activate();
        return rectCompletion.Task;
    }

    public Task<WindowCandidate?> SelectWindowAsync()
    {
        pointMode = true;
        windowCandidates = windowSelectionService.EnumerateWindows();
        HintTag.Text = "Click a window, hover to preview, Tab to cycle, Enter to capture.";
        HintTag.Visibility = Visibility.Visible;
        Show();
        Activate();
        return windowCompletion.Task;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (pointMode)
        {
            HighlightWindowAt(e.GetPosition(Root));
            CompleteWindowSelection(highlightedWindow);
            return;
        }

        selectionDrag.Begin(e.GetPosition(Root));
        isDraggingSelection = true;
        currentSelection = selectionDrag.CurrentSelection;
        HintTag.Text = "Space: move   Esc: cancel";
        HintTag.Visibility = Visibility.Visible;
        Selection.Visibility = Visibility.Visible;
        StartSelectionAnimation();
        CaptureMouse();
        UpdateSelection(currentSelection);
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var point = e.GetPosition(Root);
        UpdateCrosshair(point);

        if (pointMode)
        {
            HighlightWindowAt(point);
            return;
        }

        if (!isDraggingSelection)
        {
            return;
        }

        var end = point;
        if (Keyboard.IsKeyDown(Key.Space))
        {
            currentSelection = selectionDrag.MoveTo(end);
            BeginSelectionMove();
        }
        else
        {
            currentSelection = selectionDrag.ResizeTo(end);
            EndSelectionMove();
        }

        UpdateSelection(currentSelection);
    }

    private async void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !isDraggingSelection)
        {
            return;
        }

        ReleaseMouseCapture();
        selectionDrag.End();
        isDraggingSelection = false;
        EndSelectionMove();
        StopSelectionAnimation();

        if (currentSelection.Width < 2 || currentSelection.Height < 2)
        {
            Hide();
            rectCompletion.TrySetResult(null);
            return;
        }

        if (countdownSeconds > 0)
        {
            await ShowCountdownAsync(countdownSeconds).ConfigureAwait(true);
            if (isCancelled)
            {
                return;
            }
        }

        Hide();
        rectCompletion.TrySetResult(OverlayGeometry.ToCapturePixelRect(currentSelection, monitorBounds, Left, Top));
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Tab && pointMode)
        {
            CycleWindowHighlight((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1);
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.Enter || e.Key == Key.Return) && pointMode && highlightedWindow is { } candidate)
        {
            CompleteWindowSelection(candidate);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        isCancelled = true;
        ReleaseMouseCapture();
        selectionDrag.End();
        isDraggingSelection = false;
        EndSelectionMove();
        StopSelectionAnimation();
        Hide();
        rectCompletion.TrySetResult(null);
        windowCompletion.TrySetResult(null);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopSelectionAnimation();
        rectCompletion.TrySetResult(null);
        windowCompletion.TrySetResult(null);
        base.OnClosed(e);
    }

    private void UpdateCrosshair(WpfPoint point)
    {
        VerticalCrosshair.Visibility = Visibility.Visible;
        HorizontalCrosshair.Visibility = Visibility.Visible;
        VerticalCrosshair.X1 = point.X;
        VerticalCrosshair.X2 = point.X;
        VerticalCrosshair.Y1 = 0;
        VerticalCrosshair.Y2 = ActualHeight;
        HorizontalCrosshair.X1 = 0;
        HorizontalCrosshair.X2 = ActualWidth;
        HorizontalCrosshair.Y1 = point.Y;
        HorizontalCrosshair.Y2 = point.Y;
    }

    private void UpdateSelection(Rect rect)
    {
        Canvas.SetLeft(Selection, rect.X);
        Canvas.SetTop(Selection, rect.Y);
        Selection.Width = rect.Width;
        Selection.Height = rect.Height;
        CoordinateText.Text = $"{(int)rect.Width} x {(int)rect.Height}";
        CoordinateTag.Visibility = Visibility.Visible;
        PositionTag(CoordinateTag, new WpfPoint(rect.Right + 8, rect.Bottom + 8));
        PositionTag(HintTag, new WpfPoint(rect.X, rect.Bottom + 8));

        var intersectingMonitors = OverlayGeometry.CountIntersectingMonitors(rect, monitorBounds, Left, Top);
        if (intersectingMonitors > 1)
        {
            OverlapText.Text = $"Spans {intersectingMonitors} displays";
            OverlapTag.Visibility = Visibility.Visible;
            PositionTag(OverlapTag, new WpfPoint(rect.X, rect.Y - 32));
        }
        else
        {
            OverlapTag.Visibility = Visibility.Collapsed;
        }
    }

    private void HighlightWindowAt(WpfPoint point)
    {
        try
        {
            var screenPoint = OverlayGeometry.ToCapturePixelPoint(point, monitorBounds, Left, Top);
            windowPointer = screenPoint;
            overlappingWindowCandidates = WindowCandidatesUnderPoint(windowCandidates, screenPoint);
            var candidate = overlappingWindowCandidates.FirstOrDefault();
            if (candidate is null)
            {
                ClearWindowHighlight();
                return;
            }

            highlightedWindow = candidate;
            DrawWindowHighlight(candidate);
        }
        catch
        {
            ClearWindowHighlight();
        }
    }

    private void CycleWindowHighlight(int direction)
    {
        if (windowPointer is not { } pointer)
        {
            return;
        }

        overlappingWindowCandidates = WindowCandidatesUnderPoint(windowCandidates, pointer);
        if (overlappingWindowCandidates.Count == 0)
        {
            ClearWindowHighlight();
            return;
        }

        var currentIndex = highlightedWindow is null
            ? -1
            : Math.Max(-1, overlappingWindowCandidates.ToList().FindIndex(window => window.Hwnd == highlightedWindow.Hwnd));
        var nextIndex = currentIndex < 0
            ? direction < 0 ? overlappingWindowCandidates.Count - 1 : 0
            : (currentIndex + direction + overlappingWindowCandidates.Count) % overlappingWindowCandidates.Count;
        highlightedWindow = overlappingWindowCandidates[nextIndex];
        DrawWindowHighlight(highlightedWindow);
    }

    private void CompleteWindowSelection(WindowCandidate? candidate)
    {
        WindowCandidate? refreshedCandidate = null;
        try
        {
            refreshedCandidate = ResolveCandidateForCommit(candidate, windowSelectionService.EnumerateWindows());
        }
        catch
        {
        }

        Hide();
        windowCompletion.TrySetResult(refreshedCandidate);
    }

    private void DrawWindowHighlight(WindowCandidate candidate)
    {
        var rect = OverlayGeometry.ToOverlayRect(candidate.Bounds, monitorBounds, Left, Top);
        WindowHighlight.Visibility = Visibility.Visible;
        Canvas.SetLeft(WindowHighlight, rect.X);
        Canvas.SetTop(WindowHighlight, rect.Y);
        WindowHighlight.Width = rect.Width;
        WindowHighlight.Height = rect.Height;

        WindowLabelText.Text = string.IsNullOrWhiteSpace(candidate.Title)
            ? "Untitled window"
            : candidate.Title;
        WindowLabel.Visibility = Visibility.Visible;
        PositionTag(WindowLabel, new WpfPoint(rect.X, rect.Y - 34));
    }

    private void ClearWindowHighlight()
    {
        highlightedWindow = null;
        overlappingWindowCandidates = [];
        WindowHighlight.Visibility = Visibility.Collapsed;
        WindowLabel.Visibility = Visibility.Collapsed;
    }

    internal static IReadOnlyList<WindowCandidate> WindowCandidatesUnderPoint(
        IReadOnlyList<WindowCandidate> candidates,
        PixelPoint point) =>
        candidates.Where(candidate => Contains(candidate.Bounds, point)).ToArray();

    internal static WindowCandidate? ResolveCandidateForCommit(
        WindowCandidate? selectedCandidate,
        IReadOnlyList<WindowCandidate> refreshedCandidates) =>
        selectedCandidate is null
            ? null
            : refreshedCandidates.FirstOrDefault(candidate => candidate.Hwnd == selectedCandidate.Hwnd);

    private async Task ShowCountdownAsync(int seconds)
    {
        if (isCompleting)
        {
            return;
        }

        isCompleting = true;
        CountdownOverlay.Visibility = Visibility.Visible;
        for (var remaining = seconds; remaining > 0; remaining--)
        {
            if (isCancelled)
            {
                return;
            }

            CountdownText.Text = remaining.ToString();
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        }
    }

    private void RenderMonitorLayer()
    {
        MonitorLayer.Children.Clear();
        foreach (var monitor in monitorBounds)
        {
            var rect = OverlayGeometry.ToOverlayRect(monitor, monitorBounds, Left, Top);
            var border = new WpfRectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Stroke = (WpfBrush)FindResource("BrushRule"),
                StrokeThickness = 1,
                Fill = WpfBrushes.Transparent,
            };
            Canvas.SetLeft(border, rect.X);
            Canvas.SetTop(border, rect.Y);
            MonitorLayer.Children.Add(border);

            var label = new TextBlock
            {
                Text = monitor.IsPrimary ? $"Display {monitor.Index} primary" : $"Display {monitor.Index}",
                Background = (WpfBrush)FindResource("BrushPanel"),
                Foreground = (WpfBrush)FindResource("BrushMuted"),
                FontFamily = (WpfFontFamily)FindResource("FontUi"),
                FontSize = (double)FindResource("FontSizeSmall"),
                Padding = new Thickness(6, 3, 6, 3),
            };
            Canvas.SetLeft(label, rect.X + 8);
            Canvas.SetTop(label, rect.Y + 8);
            MonitorLayer.Children.Add(label);
        }
    }

    private void PositionTag(FrameworkElement tag, WpfPoint desired)
    {
        tag.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var point = OverlayGeometry.ClampTagPosition(
            desired,
            tag.DesiredSize,
            new WpfSize(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height));
        Canvas.SetLeft(tag, point.X);
        Canvas.SetTop(tag, point.Y);
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.X
        && point.Y >= rect.Y
        && point.X <= rect.X + rect.Width
        && point.Y <= rect.Y + rect.Height;

    private void BeginSelectionMove()
    {
        if (isMovingSelection)
        {
            return;
        }

        isMovingSelection = true;
        Cursor = System.Windows.Input.Cursors.SizeAll;
    }

    private void EndSelectionMove()
    {
        if (!isMovingSelection)
        {
            return;
        }

        isMovingSelection = false;
        Cursor = System.Windows.Input.Cursors.Cross;
    }

    private void StartSelectionAnimation()
    {
        Selection.StrokeDashOffset = 0;
        if (!selectionAntTimer.IsEnabled)
        {
            selectionAntTimer.Start();
        }
    }

    private void StopSelectionAnimation()
    {
        if (selectionAntTimer.IsEnabled)
        {
            selectionAntTimer.Stop();
        }
    }
}
