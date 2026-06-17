using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shapes;
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
    private readonly TaskCompletionSource<PixelRect?> rectCompletion = new();
    private readonly TaskCompletionSource<PixelPoint?> pointCompletion = new();
    private IReadOnlyList<WindowCandidate> windowCandidates = [];
    private WindowCandidate? highlightedWindow;
    private WpfPoint? dragStart;
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
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
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

    public Task<PixelPoint?> SelectPointAsync()
    {
        pointMode = true;
        windowCandidates = windowSelectionService.EnumerateWindows();
        HintTag.Text = "Click a window, hover to preview, Tab to cycle, Enter to capture.";
        HintTag.Visibility = Visibility.Visible;
        Show();
        Activate();
        return pointCompletion.Task;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (pointMode)
        {
            var point = highlightedWindow is { } candidate
                ? OverlayGeometry.CandidateCenter(candidate)
                : e.GetPosition(Root) + new Vector(Left, Top);
            Hide();
            pointCompletion.TrySetResult(new PixelPoint((int)Math.Round(point.X), (int)Math.Round(point.Y)));
            return;
        }

        dragStart = e.GetPosition(Root);
        currentSelection = new Rect(dragStart.Value, dragStart.Value);
        HintTag.Visibility = Visibility.Collapsed;
        Selection.Visibility = Visibility.Visible;
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

        if (dragStart is not { } start)
        {
            return;
        }

        var end = point;
        if (Keyboard.IsKeyDown(Key.Space))
        {
            var delta = end - start;
            currentSelection.Offset(delta);
            dragStart = end;
        }
        else
        {
            currentSelection = Normalize(start, end);
        }

        UpdateSelection(currentSelection);
    }

    private async void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || dragStart is null)
        {
            return;
        }

        ReleaseMouseCapture();
        dragStart = null;

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
        rectCompletion.TrySetResult(new PixelRect(
            (int)Math.Round(Left + currentSelection.X),
            (int)Math.Round(Top + currentSelection.Y),
            (int)Math.Round(currentSelection.Width),
            (int)Math.Round(currentSelection.Height)));
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
            var center = OverlayGeometry.CandidateCenter(candidate);
            Hide();
            pointCompletion.TrySetResult(new PixelPoint((int)Math.Round(center.X), (int)Math.Round(center.Y)));
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        isCancelled = true;
        ReleaseMouseCapture();
        Hide();
        rectCompletion.TrySetResult(null);
        pointCompletion.TrySetResult(null);
        e.Handled = true;
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
        var screenPoint = new PixelPoint((int)Math.Round(Left + point.X), (int)Math.Round(Top + point.Y));
        var candidate = windowSelectionService.WindowFromPoint(screenPoint)
            ?? windowCandidates.FirstOrDefault(window => Contains(window.Bounds, screenPoint));
        if (candidate is null)
        {
            ClearWindowHighlight();
            return;
        }

        highlightedWindow = candidate;
        DrawWindowHighlight(candidate);
    }

    private void CycleWindowHighlight(int direction)
    {
        if (windowCandidates.Count == 0)
        {
            return;
        }

        var currentIndex = highlightedWindow is null
            ? -1
            : Math.Max(-1, windowCandidates.ToList().FindIndex(window => window.Hwnd == highlightedWindow.Hwnd));
        var nextIndex = (currentIndex + direction + windowCandidates.Count) % windowCandidates.Count;
        highlightedWindow = windowCandidates[nextIndex];
        DrawWindowHighlight(highlightedWindow);
    }

    private void DrawWindowHighlight(WindowCandidate candidate)
    {
        var rect = OverlayGeometry.ToOverlayRect(candidate.Bounds, Left, Top);
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
        WindowHighlight.Visibility = Visibility.Collapsed;
        WindowLabel.Visibility = Visibility.Collapsed;
    }

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
            var rect = OverlayGeometry.ToOverlayRect(monitor.Bounds, Left, Top);
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

    private static Rect Normalize(WpfPoint start, WpfPoint end) =>
        new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));
}
