using System.Runtime.InteropServices;
using System.Text;

namespace Scrcap.Windows.Platform.Capture;

public sealed class WindowSelectionService : IWindowSelectionService
{
    public WindowCandidate? WindowFromPoint(PixelPoint point)
    {
        var hwnd = WindowFromPointNative(new NativePoint(point.X, point.Y));
        hwnd = GetAncestor(hwnd, GetAncestorRoot);
        return hwnd == IntPtr.Zero ? null : CandidateFor(hwnd);
    }

    public IReadOnlyList<WindowCandidate> EnumerateWindows()
    {
        var candidates = new List<WindowCandidate>();
        EnumWindows((hwnd, _) =>
        {
            if (CandidateFor(hwnd) is { } candidate)
            {
                candidates.Add(candidate);
            }

            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    private static WindowCandidate? CandidateFor(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero
            || !IsWindowVisible(hwnd)
            || IsIconic(hwnd)
            || IsShellWindow(hwnd)
            || IsToolWindow(hwnd)
            || IsCloaked(hwnd))
        {
            return null;
        }

        var bounds = WindowBounds(hwnd);
        if (bounds.Width < 40 || bounds.Height < 40)
        {
            return null;
        }

        return new WindowCandidate(hwnd, bounds, WindowTitle(hwnd));
    }

    internal static PixelRect WindowBounds(IntPtr hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmWindowAttributeExtendedFrameBounds, out NativeRect native, Marshal.SizeOf<NativeRect>()) != 0)
        {
            if (!GetWindowRect(hwnd, out native))
            {
                throw new InvalidOperationException("Could not read window bounds.");
            }
        }

        return new PixelRect(native.Left, native.Top, native.Right - native.Left, native.Bottom - native.Top);
    }

    internal static string WindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static bool IsShellWindow(IntPtr hwnd) =>
        hwnd == GetShellWindow() || hwnd == GetDesktopWindow();

    private static bool IsToolWindow(IntPtr hwnd) =>
        (GetWindowLongPtr(hwnd, GwlExStyle).ToInt64() & WsExToolWindow) != 0;

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmWindowAttributeCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const int DwmWindowAttributeExtendedFrameBounds = 9;
    private const int DwmWindowAttributeCloaked = 14;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const uint GetAncestorRoot = 2;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPointNative(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect rect, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int attributeSize);
}
