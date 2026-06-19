using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private readonly TaskCompletionSource<PixelPoint?> pointCompletion = new();
    private readonly TaskCompletionSource<WindowCandidate?> windowCompletion = new();
    private IReadOnlyList<WindowCandidate> windowCandidates = [];
    private IReadOnlyList<WindowCandidate> highlightedStack = [];
    private WindowCandidate? highlightedWindow;
    private WpfPoint? dragStart;
    private Rect currentSelection;
    private bool isMovingSelection;
    private bool pointMode;
    private bool windowPickerMode;
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
        EnsureStandaloneResources();
        InitializeComponent();
        selectionAntTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Render, (_, _) =>
        {
            SelectionAntBlack.StrokeDashOffset = (SelectionAntBlack.StrokeDashOffset + 1) % 16;
            SelectionAntWhite.StrokeDashOffset = SelectionAntBlack.StrokeDashOffset;
            SelectionAntRed.StrokeDashOffset = (SelectionAntRed.StrokeDashOffset + 1) % 28;
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
            UpdateDimMask();
            Focus();
        };
    }

    private void EnsureStandaloneResources()
    {
        if (System.Windows.Application.Current is not null || Resources.MergedDictionaries.Count > 0)
        {
            return;
        }

        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Scrcap.Windows.UI;component/Resources/ThemeTokens.xaml", UriKind.Relative),
        });
    }

    public Task<PixelRect?> SelectRegionAsync(int delayedCountdownSeconds = 0)
    {
        pointMode = false;
        windowPickerMode = false;
        countdownSeconds = Math.Max(0, delayedCountdownSeconds);
        SetRegionHint();
        HintTag.Visibility = Visibility.Visible;
        Show();
        Activate();
        return rectCompletion.Task;
    }

    public Task<PixelPoint?> SelectPointAsync()
    {
        pointMode = true;
        windowPickerMode = false;
        windowCandidates = windowSelectionService.EnumerateWindows();
        SetWindowPickerHint();
        HintTag.Visibility = Visibility.Visible;
        Show();
        Activate();
        return pointCompletion.Task;
    }

    public Task<WindowCandidate?> SelectWindowAsync()
    {
        pointMode = true;
        windowPickerMode = true;
        windowCandidates = windowSelectionService.EnumerateWindows();
        SetWindowPickerHint();
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
            CommitPointOrWindow(e.GetPosition(Root));
            return;
        }

        BeginRegionDrag(e.GetPosition(Root));
        CaptureMouse();
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
            ShowPointerReadout(point);
            return;
        }

        UpdateRegionDrag(point, Keyboard.IsKeyDown(Key.Space));
    }

    private async void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || dragStart is null)
        {
            return;
        }

        ReleaseMouseCapture();
        StopSelectionAnimation();
        if (!TryCompleteRegionDrag(out var rect))
        {
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
        rectCompletion.TrySetResult(rect);
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
            var center = CenterPoint(candidate);
            if (CandidateForCommit(center) is { } currentCandidate)
            {
                CommitWindow(currentCandidate);
            }
            else
            {
                ClearWindowHighlight();
            }

            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
        {
            return;
        }

        CancelSelection();
        rectCompletion.TrySetResult(null);
        pointCompletion.TrySetResult(null);
        windowCompletion.TrySetResult(null);
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        ReleaseMouseCapture();
        StopSelectionAnimation();
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
        SetSelectionAntRect(rect);
        UpdateDimMask(rect);
        CoordinateText.Text = $"{(int)Math.Round(rect.Width)} × {(int)Math.Round(rect.Height)}";
        CoordinateTag.Visibility = Visibility.Visible;
        PositionTag(CoordinateTag, new WpfPoint(rect.Right + 8, rect.Y - 34), new WpfPoint(rect.Right, rect.Top));

        var intersectingMonitors = OverlayGeometry.CountIntersectingMonitors(rect, monitorBounds, Left, Top);
        SetMoveHint(intersectingMonitors);
        OverlapTag.Visibility = Visibility.Visible;
        PositionTag(OverlapTag, new WpfPoint(rect.X, rect.Bottom + 8), new WpfPoint(rect.X, rect.Bottom));
    }

    internal Rect CurrentSelectionForTests => currentSelection;

    internal bool IsSelectionAnimationEnabledForTests => selectionAntTimer.IsEnabled;

    internal WindowCandidate? HighlightedWindowForTests => highlightedWindow;

    internal string WindowLabelTextForTests => WindowLabelText.Text;

    internal void BeginRegionDragForTests(WpfPoint point) => BeginRegionDrag(point);

    internal void MoveRegionDragForTests(WpfPoint point, bool moveSelection) => UpdateRegionDrag(point, moveSelection);

    internal PixelRect? CompleteRegionDragForTests() =>
        TryCompleteRegionDrag(out var rect) ? rect : null;

    internal void CancelForTests() => CancelSelection();

    internal void HighlightWindowAtForTests(WpfPoint point) => HighlightWindowAt(point);

    internal void CycleWindowHighlightForTests(int direction) => CycleWindowHighlight(direction);

    internal void CommitPointOrWindowForTests(WpfPoint point) => CommitPointOrWindow(point);

    private void BeginRegionDrag(WpfPoint point)
    {
        dragStart = point;
        isMovingSelection = false;
        currentSelection = new Rect(point, point);
        HintTag.Visibility = Visibility.Collapsed;
        CoordinateTag.Visibility = Visibility.Collapsed;
        SelectionAntLayer.Visibility = Visibility.Visible;
        StartSelectionAnimation();
        UpdateSelection(currentSelection);
    }

    private void UpdateRegionDrag(WpfPoint point, bool moveSelection)
    {
        if (dragStart is not { } start)
        {
            return;
        }

        if (moveSelection)
        {
            if (!isMovingSelection)
            {
                dragStart = point;
                isMovingSelection = true;
                UpdateSelection(currentSelection);
                return;
            }

            var delta = point - start;
            currentSelection.Offset(delta);
            dragStart = point;
        }
        else
        {
            isMovingSelection = false;
            currentSelection = Normalize(start, point);
        }

        UpdateSelection(currentSelection);
    }

    private bool TryCompleteRegionDrag(out PixelRect rect)
    {
        dragStart = null;
        isMovingSelection = false;
        StopSelectionAnimation();

        if (currentSelection.Width < 2 || currentSelection.Height < 2)
        {
            ClearSelection();
            Hide();
            rect = default;
            return false;
        }

        rect = OverlayGeometry.ToCapturePixelRect(currentSelection, monitorBounds, Left, Top);
        return true;
    }

    private void CancelSelection()
    {
        isCancelled = true;
        isMovingSelection = false;
        ReleaseMouseCapture();
        StopSelectionAnimation();
        ClearSelection();
        Hide();
    }

    private void HighlightWindowAt(WpfPoint point)
    {
        try
        {
            var screenPoint = OverlayGeometry.ToCapturePixelPoint(point, monitorBounds, Left, Top);
            var stack = WindowStackAt(screenPoint);
            if (stack.Count == 0)
            {
                ClearWindowHighlight();
                return;
            }

            highlightedStack = stack;
            highlightedWindow = stack[0];
            DrawWindowHighlight(highlightedWindow, highlightedStack);
        }
        catch
        {
            ClearWindowHighlight();
        }
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
        highlightedStack = OverlappingWindows(highlightedWindow);
        DrawWindowHighlight(highlightedWindow, highlightedStack);
    }

    private void DrawWindowHighlight(WindowCandidate candidate, IReadOnlyList<WindowCandidate>? stack = null)
    {
        var rect = OverlayGeometry.ToOverlayRect(candidate.Bounds, monitorBounds, Left, Top);
        UpdateDimMask(highlight: rect);
        WindowHighlight.Visibility = Visibility.Visible;
        Canvas.SetLeft(WindowHighlight, rect.X);
        Canvas.SetTop(WindowHighlight, rect.Y);
        WindowHighlight.Width = rect.Width;
        WindowHighlight.Height = rect.Height;

        var indexText = OverlapIndexText(candidate, stack ?? []);
        var title = string.IsNullOrWhiteSpace(candidate.Title) ? "Untitled window" : candidate.Title;
        WindowLabelText.Text = $"{indexText}{title}  {candidate.Bounds.Width} × {candidate.Bounds.Height}";
        WindowLabel.Visibility = Visibility.Visible;
        PositionTag(WindowLabel, new WpfPoint(rect.X, rect.Y - 34), new WpfPoint(rect.X, rect.Top));
    }

    private void ClearWindowHighlight()
    {
        highlightedWindow = null;
        highlightedStack = [];
        WindowHighlight.Visibility = Visibility.Collapsed;
        WindowLabel.Visibility = Visibility.Collapsed;
        UpdateDimMask();
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

    private void PositionTag(FrameworkElement tag, WpfPoint desired, WpfPoint? anchor = null)
    {
        tag.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        var bounds = anchor is { } anchorPoint
            ? OverlayGeometry.MonitorOverlayBoundsFor(anchorPoint, monitorBounds, Left, Top)
            : new Rect(0, 0, ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);
        var point = OverlayGeometry.ClampTagPosition(
            desired,
            tag.DesiredSize,
            bounds);
        Canvas.SetLeft(tag, point.X);
        Canvas.SetTop(tag, point.Y);
    }

    private void UpdateDimMask(Rect? selection = null, Rect? highlight = null)
    {
        var width = Math.Max(1, ActualWidth > 0 ? ActualWidth : Width);
        var height = Math.Max(1, ActualHeight > 0 ? ActualHeight : Height);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, width, height)));

        var selectionRect = selection ?? (SelectionAntLayer.Visibility == Visibility.Visible ? currentSelection : Rect.Empty);
        if (selectionRect.Width > 0 && selectionRect.Height > 0)
        {
            geometry.Children.Add(new RectangleGeometry(selectionRect));
        }

        var highlightRect = highlight
            ?? (WindowHighlight.Visibility == Visibility.Visible
                ? new Rect(Canvas.GetLeft(WindowHighlight), Canvas.GetTop(WindowHighlight), WindowHighlight.Width, WindowHighlight.Height)
                : Rect.Empty);
        if (highlightRect.Width > 0 && highlightRect.Height > 0)
        {
            geometry.Children.Add(new RectangleGeometry(highlightRect));
        }

        DimMask.Data = geometry;
    }

    private void SetSelectionAntRect(Rect rect)
    {
        foreach (var ant in new[] { SelectionAntBlack, SelectionAntWhite, SelectionAntRed })
        {
            Canvas.SetLeft(ant, rect.X);
            Canvas.SetTop(ant, rect.Y);
            ant.Width = rect.Width;
            ant.Height = rect.Height;
        }
    }

    private void ShowPointerReadout(WpfPoint point)
    {
        var screenPoint = OverlayGeometry.ToCapturePixelPoint(point, monitorBounds, Left, Top);
        CoordinateText.Text = $"{screenPoint.X}, {screenPoint.Y}";
        CoordinateTag.Visibility = Visibility.Visible;
        PositionTag(CoordinateTag, new WpfPoint(point.X + 12, point.Y + 12), point);
    }

    private void ClearSelection()
    {
        SelectionAntLayer.Visibility = Visibility.Collapsed;
        CoordinateTag.Visibility = Visibility.Collapsed;
        OverlapTag.Visibility = Visibility.Collapsed;
        currentSelection = Rect.Empty;
        UpdateDimMask();
    }

    private void CommitPointOrWindow(WpfPoint overlayPoint)
    {
        var screenPoint = OverlayGeometry.ToCapturePixelPoint(overlayPoint, monitorBounds, Left, Top);
        var candidate = CandidateForCommit(screenPoint);
        if (windowPickerMode)
        {
            if (candidate is null)
            {
                ClearWindowHighlight();
                return;
            }

            CommitWindow(candidate);
            return;
        }

        var point = candidate is { }
            ? CenterPoint(candidate)
            : screenPoint;
        Hide();
        pointCompletion.TrySetResult(point);
    }

    private void CommitWindow(WindowCandidate candidate)
    {
        Hide();
        pointCompletion.TrySetResult(CenterPoint(candidate));
        windowCompletion.TrySetResult(candidate);
    }

    private WindowCandidate? CandidateForCommit(PixelPoint screenPoint)
    {
        var currentCandidates = windowSelectionService.EnumerateWindows();
        return ResolveCandidateForCommit(
            highlightedWindow,
            screenPoint,
            currentCandidates,
            windowSelectionService.WindowFromPoint(screenPoint));
    }

    private IReadOnlyList<WindowCandidate> WindowStackAt(PixelPoint point) =>
        WindowStackAt(windowCandidates, point);

    private IReadOnlyList<WindowCandidate> OverlappingWindows(WindowCandidate candidate) =>
        windowCandidates
            .Where(window => Intersects(window.Bounds, candidate.Bounds))
            .ToArray();

    internal static IReadOnlyList<WindowCandidate> WindowStackAt(IReadOnlyList<WindowCandidate> candidates, PixelPoint point) =>
        candidates
            .Where(window => Contains(window.Bounds, point))
            .ToArray();

    internal static WindowCandidate? ResolveCandidateForCommit(
        WindowCandidate? selectedCandidate,
        PixelPoint point,
        IReadOnlyList<WindowCandidate> currentCandidates,
        WindowCandidate? livePointCandidate)
    {
        if (selectedCandidate is { } selected)
        {
            if (currentCandidates.FirstOrDefault(candidate => candidate.Hwnd == selected.Hwnd) is { } refreshed)
            {
                return refreshed;
            }

            return livePointCandidate;
        }

        return livePointCandidate ?? WindowStackAt(currentCandidates, point).FirstOrDefault();
    }

    private static string OverlapIndexText(WindowCandidate candidate, IReadOnlyList<WindowCandidate> stack)
    {
        if (stack.Count <= 1)
        {
            return string.Empty;
        }

        var index = Math.Max(0, stack.ToList().FindIndex(window => window.Hwnd == candidate.Hwnd));
        return $"{index + 1}/{stack.Count}  ";
    }

    private void SetRegionHint()
    {
        HintTagText.Inlines.Clear();
        HintTagText.Inlines.Add(new Run(countdownSeconds > 0
            ? $"Drag a region, then wait {countdownSeconds}s. "
            : "Drag a region. "));
        AddKeycap(HintTagText, "Space");
        HintTagText.Inlines.Add(new Run(" move  "));
        AddKeycap(HintTagText, "Esc");
        HintTagText.Inlines.Add(new Run(" cancel"));
    }

    private void SetWindowPickerHint()
    {
        HintTagText.Inlines.Clear();
        HintTagText.Inlines.Add(new Run("Hover a window. Tab cycles, Enter captures, "));
        AddKeycap(HintTagText, "Esc");
        HintTagText.Inlines.Add(new Run(" cancels"));
    }

    private void SetMoveHint(int intersectingMonitors)
    {
        OverlapText.Inlines.Clear();
        AddKeycap(OverlapText, "Space");
        OverlapText.Inlines.Add(new Run(" move  "));
        AddKeycap(OverlapText, "Esc");
        OverlapText.Inlines.Add(new Run(" cancel"));
        if (intersectingMonitors > 1)
        {
            OverlapText.Inlines.Add(new Run($"  Spans {intersectingMonitors} displays"));
        }
    }

    private void AddKeycap(TextBlock textBlock, string text)
    {
        textBlock.Inlines.Add(new InlineUIContainer(new Border
        {
            Background = (WpfBrush)FindResource("BrushAccent"),
            BorderBrush = (WpfBrush)FindResource("BrushAccentDeep"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            Child = new TextBlock
            {
                Text = text,
                Foreground = (WpfBrush)FindResource("BrushOnAccent"),
                FontFamily = (WpfFontFamily)FindResource("FontMono"),
                FontSize = (double)FindResource("FontSizeSmall"),
            },
        }));
    }

    private static bool Contains(PixelRect rect, PixelPoint point) =>
        point.X >= rect.X
        && point.Y >= rect.Y
        && point.X <= rect.X + rect.Width
        && point.Y <= rect.Y + rect.Height;

    private static bool Intersects(PixelRect first, PixelRect second) =>
        first.X < second.Right
        && first.Right > second.X
        && first.Y < second.Bottom
        && first.Bottom > second.Y;

    private static PixelPoint CenterPoint(WindowCandidate candidate)
    {
        var center = OverlayGeometry.CandidateCenter(candidate);
        return new PixelPoint((int)Math.Round(center.X), (int)Math.Round(center.Y));
    }

    private static Rect Normalize(WpfPoint start, WpfPoint end) =>
        new(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Abs(start.X - end.X), Math.Abs(start.Y - end.Y));

    private void StartSelectionAnimation()
    {
        SelectionAntBlack.StrokeDashOffset = 0;
        SelectionAntWhite.StrokeDashOffset = 0;
        SelectionAntRed.StrokeDashOffset = 0;
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
