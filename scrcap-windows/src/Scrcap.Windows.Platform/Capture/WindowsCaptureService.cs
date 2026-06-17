using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Scrcap.Core;

namespace Scrcap.Windows.Platform.Capture;

public sealed class WindowsCaptureService : IWindowsCaptureService
{
    private readonly WindowsGraphicsCaptureBackend graphicsCapture;
    private readonly GdiCaptureFallback gdiFallback;

    public WindowsCaptureService()
        : this(new WindowsGraphicsCaptureBackend(), new GdiCaptureFallback())
    {
    }

    internal WindowsCaptureService(WindowsGraphicsCaptureBackend graphicsCapture, GdiCaptureFallback gdiFallback)
    {
        this.graphicsCapture = graphicsCapture;
        this.gdiFallback = gdiFallback;
    }

    public async Task<CaptureResult> CaptureRegionAsync(PixelRect rect, CaptureRequest request, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        using var bitmap = await CaptureRegionBitmapAsync(rect, request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        return new CaptureResult(
            EncodePng(bitmap),
            rect.Width,
            rect.Height,
            new CaptureMetadata(CaptureMode.Region, null, rect, DateTimeOffset.Now));
    }

    public Task<CaptureResult> CaptureActiveWindowAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("No active window is available to capture.");
        }

        return CaptureWindowAsync(hwnd, request, cancellationToken);
    }

    public async Task<CaptureResult> CaptureWindowAsync(IntPtr hwnd, CaptureRequest request, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        var rect = WindowSelectionService.WindowBounds(hwnd);
        using var bitmap = await CaptureWindowBitmapAsync(hwnd, rect, request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        using var composed = ComposeWindowBitmap(bitmap, request);
        return new CaptureResult(
            EncodePng(composed),
            composed.Width,
            composed.Height,
            new CaptureMetadata(CaptureMode.Window, WindowSelectionService.WindowTitle(hwnd), rect, DateTimeOffset.Now));
    }

    public async Task<CaptureResult> CaptureMonitorUnderCursorAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        var monitor = MonitorSelection.FromPoint(Cursor.Position);
        using var bitmap = await CaptureMonitorBitmapAsync(monitor, request.IncludeCursor, cancellationToken).ConfigureAwait(false);
        return new CaptureResult(
            EncodePng(bitmap),
            bitmap.Width,
            bitmap.Height,
            new CaptureMetadata(CaptureMode.Fullscreen, monitor.DeviceName, monitor.Bounds, DateTimeOffset.Now));
    }

    public async Task<CaptureResult> CaptureScrollingRegionAsync(PixelRect rect, CaptureRequest request, ScrollingCaptureOptions options, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        var frames = new List<Bitmap>();
        var accumulatedHashes = new List<ulong>();
        IReadOnlyList<ulong>? firstFrameHashes = null;
        IReadOnlyList<ulong>? previousFrameHashes = null;
        var bottomProbeCount = 0;
        var maxRows = Math.Max(rect.Height, options.MaxHeight);

        try
        {
            while (TotalHeight(frames) < maxRows && EstimatedBytes(frames, rect.Width) < options.MemoryCapBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var rawFrame = await CaptureRegionBitmapAsync(rect, request.IncludeCursor && frames.Count == 0, cancellationToken).ConfigureAwait(false);
                var frame = CloneBitmap(rawFrame);
                var hashes = RowHashes(frame);

                if (frames.Count == 0)
                {
                    frames.Add(frame);
                    firstFrameHashes = hashes;
                    previousFrameHashes = hashes;
                    accumulatedHashes.AddRange(hashes);
                    options.Progress?.Report(new ScrollingCaptureProgress(frames.Count, TotalHeight(frames), maxRows));
                }
                else
                {
                    var stickyRows = ScrollingFramePlanner.DetectStickyHeaderRows(firstFrameHashes ?? [], hashes, options.StickyHeaderMaxPixels);
                    var newContentStart = ScrollingFramePlanner.FindNewContentStart(accumulatedHashes, hashes, stickyRows);

                    if (newContentStart is null || newContentStart.Value >= frame.Height)
                    {
                        frame.Dispose();
                        bottomProbeCount++;
                    }
                    else
                    {
                        var top = Math.Clamp(newContentStart.Value, 0, frame.Height - 1);
                        frames.Add(CropBitmap(frame, new Rectangle(0, top, frame.Width, frame.Height - top)));
                        accumulatedHashes.AddRange(hashes.Skip(top));
                        frame.Dispose();
                        bottomProbeCount = 0;
                        options.Progress?.Report(new ScrollingCaptureProgress(frames.Count, TotalHeight(frames), maxRows));
                    }

                    if (previousFrameHashes is not null && ScrollingFramePlanner.RowsEqual(previousFrameHashes, hashes))
                    {
                        bottomProbeCount++;
                    }

                    previousFrameHashes = hashes;
                }

                if (bottomProbeCount >= options.BottomProbeFrames)
                {
                    break;
                }

                ScrollDown(rect, Math.Max(1, (int)Math.Floor(rect.Height * options.ScrollStepRatio)));
                await WaitForScrollSettleAsync(rect, request, options, cancellationToken).ConfigureAwait(false);
            }

            using var stitched = StitchFrames(frames, rect.Width, Math.Min(options.MaxHeight, TotalHeight(frames)));
            return new CaptureResult(
                EncodePng(stitched),
                stitched.Width,
                stitched.Height,
                new CaptureMetadata(CaptureMode.Scrolling, null, rect, DateTimeOffset.Now));
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private async Task<Bitmap> CaptureRegionBitmapAsync(PixelRect rect, bool includeCursor, CancellationToken cancellationToken)
    {
        ValidateRect(rect);
        var monitor = MonitorSelection.FromRect(rect);
        if (!monitor.Bounds.Contains(rect))
        {
            return gdiFallback.CaptureScreenRect(rect, includeCursor);
        }

        try
        {
            using var monitorBitmap = await graphicsCapture.CaptureMonitorAsync(monitor.Handle, includeCursor, cancellationToken).ConfigureAwait(false);
            var crop = rect.RelativeTo(monitor.Bounds);
            return CropBitmap(monitorBitmap, new Rectangle(crop.X, crop.Y, crop.Width, crop.Height));
        }
        catch (Exception exception) when (WindowsGraphicsCaptureBackend.CanFallback(exception))
        {
            return gdiFallback.CaptureScreenRect(rect, includeCursor);
        }
    }

    private async Task<Bitmap> CaptureWindowBitmapAsync(IntPtr hwnd, PixelRect rect, bool includeCursor, CancellationToken cancellationToken)
    {
        ValidateRect(rect);
        try
        {
            return await graphicsCapture.CaptureWindowAsync(hwnd, includeCursor, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (WindowsGraphicsCaptureBackend.CanFallback(exception))
        {
            return gdiFallback.CaptureScreenRect(rect, includeCursor);
        }
    }

    private async Task<Bitmap> CaptureMonitorBitmapAsync(MonitorInfo monitor, bool includeCursor, CancellationToken cancellationToken)
    {
        try
        {
            return await graphicsCapture.CaptureMonitorAsync(monitor.Handle, includeCursor, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (WindowsGraphicsCaptureBackend.CanFallback(exception))
        {
            return gdiFallback.CaptureScreenRect(monitor.Bounds, includeCursor);
        }
    }

    private async Task WaitForScrollSettleAsync(PixelRect rect, CaptureRequest request, ScrollingCaptureOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<ulong>? previousProbe = null;
        var stableProbes = 0;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(options.SettleTimeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(options.SettlePollMilliseconds, cancellationToken).ConfigureAwait(false);

            using var probe = await CaptureRegionBitmapAsync(rect, request.IncludeCursor, cancellationToken).ConfigureAwait(false);
            var hashes = RowHashes(probe);
            if (previousProbe is not null && ScrollingFramePlanner.RowsEqual(previousProbe, hashes))
            {
                stableProbes++;
                if (stableProbes >= options.RequiredStableSettleSamples)
                {
                    return;
                }
            }
            else
            {
                stableProbes = 0;
            }

            previousProbe = hashes;
        }
    }

    private static Task DelayIfNeeded(CaptureRequest request, CancellationToken cancellationToken) =>
        request.DelaySeconds > 0
            ? Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds), cancellationToken)
            : Task.CompletedTask;

    private static void ValidateRect(PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Capture rectangle must have positive dimensions.");
        }
    }

    private static Bitmap ComposeWindowBitmap(Bitmap bitmap, CaptureRequest request)
    {
        if (request.WindowBackgroundTransparent && !request.IncludeWindowShadow)
        {
            return CloneBitmap(bitmap);
        }

        var pad = request.IncludeWindowShadow ? 20 : 0;
        var composed = new Bitmap(bitmap.Width + pad * 2, bitmap.Height + pad * 2, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(composed);
        graphics.Clear(ColorTranslator.FromHtml(request.WindowBackgroundHex));
        if (request.IncludeWindowShadow)
        {
            using var shadow = new SolidBrush(Color.FromArgb(42, 0, 0, 0));
            graphics.FillRectangle(shadow, pad / 2, pad / 2, bitmap.Width + pad, bitmap.Height + pad);
        }

        graphics.DrawImageUnscaled(bitmap, pad, pad);
        return composed;
    }

    private static IReadOnlyList<ulong> RowHashes(Bitmap bitmap)
    {
        var hashes = new ulong[bitmap.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                hashes[y] = StitchEngine.RowHash(row.AsSpan(0, bitmap.Width * 4));
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return hashes;
    }

    private static Bitmap StitchFrames(IReadOnlyList<Bitmap> frames, int width, int maxHeight)
    {
        if (frames.Count == 0)
        {
            throw new InvalidOperationException("Scrolling capture did not produce a valid frame.");
        }

        var height = Math.Min(maxHeight, TotalHeight(frames));
        var stitched = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(stitched);
        var y = 0;
        foreach (var frame in frames)
        {
            var remaining = height - y;
            if (remaining <= 0)
            {
                break;
            }

            var sourceHeight = Math.Min(frame.Height, remaining);
            graphics.DrawImage(frame, new Rectangle(0, y, width, sourceHeight), new Rectangle(0, 0, width, sourceHeight), GraphicsUnit.Pixel);
            y += sourceHeight;
        }

        return stitched;
    }

    private static Bitmap CropBitmap(Bitmap bitmap, Rectangle rect)
    {
        var clone = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImage(bitmap, new Rectangle(0, 0, rect.Width, rect.Height), rect, GraphicsUnit.Pixel);
        return clone;
    }

    private static Bitmap CloneBitmap(Bitmap bitmap)
    {
        var clone = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(clone);
        graphics.DrawImageUnscaled(bitmap, 0, 0);
        return clone;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.SetResolution(96, 96);
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static int TotalHeight(IEnumerable<Bitmap> frames) => frames.Sum(frame => frame.Height);

    private static long EstimatedBytes(IEnumerable<Bitmap> frames, int width) =>
        frames.Sum(frame => (long)frame.Height * width * 4);

    private static void ScrollDown(PixelRect rect, int pixels)
    {
        SetCursorPos(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var input = new Input
        {
            Type = InputMouse,
            MouseInput = new MouseInput
            {
                Flags = MouseeventfWheel,
                MouseData = unchecked((uint)-Math.Max(120, pixels)),
            },
        };
        SendInput(1, [input], Marshal.SizeOf<Input>());
    }

    private const int InputMouse = 0;
    private const uint MouseeventfWheel = 0x0800;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public MouseInput MouseInput;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
