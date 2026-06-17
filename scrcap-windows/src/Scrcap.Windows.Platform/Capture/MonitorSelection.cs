using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Scrcap.Windows.Platform.Capture;

internal static class MonitorSelection
{
    public static MonitorInfo FromPoint(Point point) =>
        FromHandle(MonitorFromPoint(new NativePoint(point.X, point.Y), MonitorDefaultToNearest));

    public static MonitorInfo FromRect(PixelRect rect)
    {
        var native = new NativeRect
        {
            Left = rect.X,
            Top = rect.Y,
            Right = rect.Right,
            Bottom = rect.Bottom,
        };
        return FromHandle(MonitorFromRect(ref native, MonitorDefaultToNearest));
    }

    private static MonitorInfo FromHandle(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            var screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("No monitor is available.");
            return new MonitorInfo(
                IntPtr.Zero,
                new PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
                screen.DeviceName);
        }

        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
        };

        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Could not read monitor bounds.");
        }

        return new MonitorInfo(
            monitor,
            new PixelRect(
                info.Monitor.Left,
                info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left,
                info.Monitor.Bottom - info.Monitor.Top),
            info.DeviceName.ToString());
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int DeviceNameChars = 32;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = DeviceNameChars)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
