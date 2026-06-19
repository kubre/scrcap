using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Scrcap.Windows.UI.Chrome;

public static class WindowChromeBehavior
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(WindowChromeBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static readonly DependencyProperty IsSnapLayoutMaximizeTargetProperty =
        DependencyProperty.RegisterAttached(
            "IsSnapLayoutMaximizeTarget",
            typeof(bool),
            typeof(WindowChromeBehavior),
            new PropertyMetadata(false));

    private const int WmNcHitTest = 0x0084;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int HtMaxButton = 9;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmWindowCornerPreferenceRound = 2;

    public static void SetEnabled(Window window, bool value) => window.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Window window) => (bool)window.GetValue(EnabledProperty);

    public static void SetIsSnapLayoutMaximizeTarget(DependencyObject element, bool value) =>
        element.SetValue(IsSnapLayoutMaximizeTargetProperty, value);

    public static bool GetIsSnapLayoutMaximizeTarget(DependencyObject element) =>
        (bool)element.GetValue(IsSnapLayoutMaximizeTargetProperty);

    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Window window || e.NewValue is not true)
        {
            return;
        }

        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 36,
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });

        window.SourceInitialized += (_, _) =>
        {
            ApplyDwmAttributes(window);
            if (PresentationSource.FromVisual(window) is HwndSource source)
            {
                source.AddHook(WindowProc);
            }
        };
        window.Loaded += (_, _) => ApplyDwmAttributes(window);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmGetMinMaxInfo)
        {
            handled = ApplyTaskbarAwareMaximizeBounds(hwnd, lParam);
            return IntPtr.Zero;
        }

        if (message != WmNcHitTest || HwndSource.FromHwnd(hwnd)?.RootVisual is not Window window)
        {
            return IntPtr.Zero;
        }

        if (FindSnapLayoutMaximizeTarget(window) is not { } maximizeTarget)
        {
            return IntPtr.Zero;
        }

        var point = maximizeTarget.PointFromScreen(new System.Windows.Point(GetX(lParam), GetY(lParam)));
        if (new Rect(0, 0, maximizeTarget.ActualWidth, maximizeTarget.ActualHeight).Contains(point))
        {
            handled = true;
            return new IntPtr(HtMaxButton);
        }

        return IntPtr.Zero;
    }

    private static FrameworkElement? FindSnapLayoutMaximizeTarget(DependencyObject root)
    {
        if (root is FrameworkElement element && GetIsSnapLayoutMaximizeTarget(element))
        {
            return element;
        }

        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            if (FindSnapLayoutMaximizeTarget(VisualTreeHelper.GetChild(root, index)) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static void ApplyDwmAttributes(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!SystemParameters.HighContrast)
        {
            var useDarkMode = ShouldUseDarkMode(window) ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20h1, ref useDarkMode, sizeof(int));
            }
        }

        var cornerPreference = DwmWindowCornerPreferenceRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
    }

    private static bool ShouldUseDarkMode(Window window) =>
        window.Background is SolidColorBrush brush
            && ((brush.Color.R * 0.2126) + (brush.Color.G * 0.7152) + (brush.Color.B * 0.0722)) < 128;

    private static bool ApplyTaskbarAwareMaximizeBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        minMaxInfo.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        minMaxInfo.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        minMaxInfo.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        minMaxInfo.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        Marshal.StructureToPtr(minMaxInfo, lParam, true);
        return true;
    }

    private static int GetX(IntPtr lParam) => unchecked((short)((long)lParam & 0xffff));

    private static int GetY(IntPtr lParam) => unchecked((short)(((long)lParam >> 16) & 0xffff));

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
}
