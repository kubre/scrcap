using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace Scrcap.Windows.Platform.Capture;

internal sealed class WindowsGraphicsCaptureBackend
{
    private readonly IFrameConverter frameConverter;

    public WindowsGraphicsCaptureBackend()
        : this(new SoftwareBitmapFrameConverter())
    {
    }

    internal WindowsGraphicsCaptureBackend(IFrameConverter frameConverter)
    {
        this.frameConverter = frameConverter;
    }

    public async Task<Bitmap> CaptureWindowAsync(IntPtr hwnd, bool includeCursor, CancellationToken cancellationToken)
    {
        if (hwnd == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle is required.", nameof(hwnd));
        }

        var item = GraphicsCaptureInterop.CreateForWindow(hwnd);
        return await CaptureItemAsync(item, includeCursor, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Bitmap> CaptureMonitorAsync(IntPtr monitor, bool includeCursor, CancellationToken cancellationToken)
    {
        if (monitor == IntPtr.Zero)
        {
            throw new ArgumentException("Monitor handle is required.", nameof(monitor));
        }

        var item = GraphicsCaptureInterop.CreateForMonitor(monitor);
        return await CaptureItemAsync(item, includeCursor, cancellationToken).ConfigureAwait(false);
    }

    public static bool CanFallback(Exception exception) =>
        exception is NotSupportedException
            or COMException
            or TimeoutException
            or InvalidOperationException
            or UnauthorizedAccessException;

    private async Task<Bitmap> CaptureItemAsync(GraphicsCaptureItem item, bool includeCursor, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) || !GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this Windows version.");
        }

        using var direct3DDevice = Direct3DDeviceFactory.Create();
        var frameReady = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            direct3DDevice.Device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            item.Size);
        var session = framePool.CreateCaptureSession(item);

        try
        {
            TrySetCursorCapture(session, includeCursor);
            framePool.FrameArrived += (pool, _) =>
            {
                try
                {
                    if (pool.TryGetNextFrame() is { } frame)
                    {
                        frameReady.TrySetResult(frame);
                    }
                }
                catch (Exception exception)
                {
                    frameReady.TrySetException(exception);
                }
            };

            session.StartCapture();
            using var frame = await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            return await frameConverter.ConvertAsync(frame.Surface, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            DisposeIfNeeded(session);
            DisposeIfNeeded(framePool);
        }
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool includeCursor)
    {
        try
        {
            session.IsCursorCaptureEnabled = includeCursor;
        }
        catch (COMException)
        {
            // Older Windows builds can expose WGC without cursor toggling.
        }
    }

    private sealed class Direct3DDeviceFactory : IDisposable
    {
        private readonly IntPtr d3dDevice;
        private readonly IntPtr d3dContext;
        private readonly IntPtr dxgiDevice;
        private readonly IntPtr winRtDevice;

        private Direct3DDeviceFactory(IntPtr d3dDevice, IntPtr d3dContext, IntPtr dxgiDevice, IntPtr winRtDevice, IDirect3DDevice device)
        {
            this.d3dDevice = d3dDevice;
            this.d3dContext = d3dContext;
            this.dxgiDevice = dxgiDevice;
            this.winRtDevice = winRtDevice;
            Device = device;
        }

        public IDirect3DDevice Device { get; }

        public static Direct3DDeviceFactory Create()
        {
            var featureLevels = new[]
            {
                D3DFeatureLevel.Level111,
                D3DFeatureLevel.Level110,
                D3DFeatureLevel.Level101,
                D3DFeatureLevel.Level100,
            };

            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                D3DDriverType.Hardware,
                IntPtr.Zero,
                D3D11CreateDeviceBgraSupport,
                featureLevels,
                featureLevels.Length,
                D3D11SdkVersion,
                out var d3dDevice,
                out _,
                out var d3dContext);
            ThrowIfFailed(hr, "D3D11CreateDevice failed.");

            hr = Marshal.QueryInterface(d3dDevice, ref IidDxgiDevice, out var dxgiDevice);
            ThrowIfFailed(hr, "Could not query IDXGIDevice.");

            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var winRtDevice);
            ThrowIfFailed(hr, "CreateDirect3D11DeviceFromDXGIDevice failed.");

            var device = MarshalInterface<IDirect3DDevice>.FromAbi(winRtDevice);
            return new Direct3DDeviceFactory(d3dDevice, d3dContext, dxgiDevice, winRtDevice, device);
        }

        public void Dispose()
        {
            DisposeIfNeeded(Device);
            ReleaseIfNeeded(winRtDevice);
            ReleaseIfNeeded(dxgiDevice);
            ReleaseIfNeeded(d3dContext);
            ReleaseIfNeeded(d3dDevice);
        }

        private const int D3D11SdkVersion = 7;
        private const uint D3D11CreateDeviceBgraSupport = 0x20;
        private static Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

        private enum D3DDriverType : uint
        {
            Hardware = 1,
        }

        private enum D3DFeatureLevel : uint
        {
            Level100 = 0xa000,
            Level101 = 0xa100,
            Level110 = 0xb000,
            Level111 = 0xb100,
        }

        [DllImport("d3d11.dll", PreserveSig = true)]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            D3DDriverType driverType,
            IntPtr software,
            uint flags,
            D3DFeatureLevel[] featureLevels,
            int featureLevelsCount,
            int sdkVersion,
            out IntPtr device,
            out D3DFeatureLevel featureLevel,
            out IntPtr immediateContext);

        [DllImport("d3d11.dll", PreserveSig = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    }

    private static void ThrowIfFailed(int hr, string message)
    {
        if (hr < 0)
        {
            throw new COMException(message, hr);
        }
    }

    private static void ReleaseIfNeeded(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            Marshal.Release(value);
        }
    }

    private static void DisposeIfNeeded(object value)
    {
        if (value is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static class GraphicsCaptureInterop
    {
        public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
        {
            var iid = IidGraphicsCaptureItem;
            using var factory = GraphicsCaptureItemFactory.Create();
            ThrowIfFailed(factory.Interop.CreateForWindow(hwnd, ref iid, out var item), "CreateForWindow failed.");
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }

        public static GraphicsCaptureItem CreateForMonitor(IntPtr monitor)
        {
            var iid = IidGraphicsCaptureItem;
            using var factory = GraphicsCaptureItemFactory.Create();
            ThrowIfFailed(factory.Interop.CreateForMonitor(monitor, ref iid, out var item), "CreateForMonitor failed.");
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(item);
        }

        private static readonly Guid IidGraphicsCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");

        private sealed class GraphicsCaptureItemFactory : IDisposable
        {
            private readonly IntPtr classId;
            private readonly IntPtr factoryPointer;

            private GraphicsCaptureItemFactory(IntPtr classId, IntPtr factoryPointer, IGraphicsCaptureItemInterop interop)
            {
                this.classId = classId;
                this.factoryPointer = factoryPointer;
                Interop = interop;
            }

            public IGraphicsCaptureItemInterop Interop { get; }

            public static GraphicsCaptureItemFactory Create()
            {
                const string activatableClass = "Windows.Graphics.Capture.GraphicsCaptureItem";
                ThrowIfFailed(WindowsCreateString(activatableClass, activatableClass.Length, out var classId), "WindowsCreateString failed.");
                try
                {
                    var iid = IidGraphicsCaptureItemInterop;
                    ThrowIfFailed(RoGetActivationFactory(classId, ref iid, out var factoryPointer), "RoGetActivationFactory failed.");
                    var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPointer);
                    return new GraphicsCaptureItemFactory(classId, factoryPointer, interop);
                }
                catch
                {
                    WindowsDeleteString(classId);
                    throw;
                }
            }

            public void Dispose()
            {
                ReleaseIfNeeded(factoryPointer);
                WindowsDeleteString(classId);
            }
        }

        private static readonly Guid IidGraphicsCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");

        [ComImport]
        [Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            [PreserveSig]
            int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr result);

            [PreserveSig]
            int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr result);
        }

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

        [DllImport("combase.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

        [DllImport("combase.dll", PreserveSig = true)]
        private static extern int WindowsDeleteString(IntPtr hstring);
    }
}

internal interface IFrameConverter
{
    Task<Bitmap> ConvertAsync(IDirect3DSurface surface, CancellationToken cancellationToken);
}

internal sealed class SoftwareBitmapFrameConverter : IFrameConverter
{
    public async Task<Bitmap> ConvertAsync(IDirect3DSurface surface, CancellationToken cancellationToken)
    {
        using var source = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).AsTask(cancellationToken).ConfigureAwait(false);
        using var bitmap = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var bytes = new byte[width * height * 4];
        var buffer = new global::Windows.Storage.Streams.Buffer((uint)bytes.Length);
        bitmap.CopyToBuffer(buffer);
        using (var reader = DataReader.FromBuffer(buffer))
        {
            reader.ReadBytes(bytes);
        }

        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            if (stride == width * 4)
            {
                Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(bytes, y * width * 4, data.Scan0 + y * data.Stride, width * 4);
                }
            }
        }
        finally
        {
            result.UnlockBits(data);
        }

        return result;
    }
}
