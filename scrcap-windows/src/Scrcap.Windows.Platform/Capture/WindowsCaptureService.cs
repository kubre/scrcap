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
    private readonly Func<PixelRect, CaptureRequest, CancellationToken, Task<BackendCapture>>? captureRegionBitmapOverride;
    private readonly Action<PixelRect, int>? scrollDownOverride;

    public WindowsCaptureService()
        : this(new WindowsGraphicsCaptureBackend(), new GdiCaptureFallback())
    {
    }

    internal WindowsCaptureService(WindowsGraphicsCaptureBackend graphicsCapture, GdiCaptureFallback gdiFallback)
    {
        this.graphicsCapture = graphicsCapture;
        this.gdiFallback = gdiFallback;
    }

    internal WindowsCaptureService(
        Func<PixelRect, CaptureRequest, CancellationToken, Task<Bitmap>> captureRegionBitmap,
        Action<PixelRect, int>? scrollDown = null)
        : this(new WindowsGraphicsCaptureBackend(), new GdiCaptureFallback())
    {
        captureRegionBitmapOverride = async (rect, request, token) =>
            new BackendCapture(await captureRegionBitmap(rect, request, token).ConfigureAwait(false), CaptureBackendUsed.Gdi, null);
        scrollDownOverride = scrollDown;
    }

    public async Task<CaptureResult> CaptureRegionAsync(PixelRect rect, CaptureRequest request, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        using var capture = await CaptureRegionBitmapAsync(rect, request, cancellationToken).ConfigureAwait(false);
        var metadata = Metadata(CaptureMode.Region, null, rect, request, capture);
        return CreateResult(capture.Bitmap, metadata);
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
        using var capture = await CaptureWindowBitmapAsync(hwnd, rect, request, cancellationToken).ConfigureAwait(false);
        using var composed = WindowBitmapComposer.Compose(capture.Bitmap, request);
        var captureBounds = request.IncludeWindowShadow
            ? new PixelRect(rect.X - WindowBitmapComposer.ShadowPadding, rect.Y - WindowBitmapComposer.ShadowPadding, composed.Width, composed.Height)
            : rect;
        var metadata = Metadata(CaptureMode.Window, WindowSelectionService.WindowTitle(hwnd), rect, request, capture, captureBounds);
        return CreateResult(composed, metadata);
    }

    public async Task<CaptureResult> CaptureMonitorUnderCursorAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        var monitor = MonitorSelection.FromPoint(Cursor.Position);
        using var capture = await CaptureMonitorBitmapAsync(monitor, request, cancellationToken).ConfigureAwait(false);
        var metadata = Metadata(CaptureMode.Fullscreen, monitor.DeviceName, monitor.Bounds, request, capture);
        return CreateResult(capture.Bitmap, metadata);
    }

    public async Task<CaptureResult> CaptureScrollingRegionAsync(PixelRect rect, CaptureRequest request, ScrollingCaptureOptions options, CancellationToken cancellationToken)
    {
        await DelayIfNeeded(request, cancellationToken).ConfigureAwait(false);
        var frames = new List<Bitmap>();
        var accumulatedHashes = new List<ulong>();
        IReadOnlyList<ulong>? firstFrameHashes = null;
        IReadOnlyList<ulong>? previousFrameHashes = null;
        var bottomProbeCount = 0;
        var maxRows = Math.Max(1, options.MaxHeight);
        var backendUsed = CaptureBackendUsed.WindowsGraphicsCapture;
        string? fallbackReason = null;
        var stopReason = ScrollingCaptureStopReason.Completed;

        try
        {
            while (true)
            {
                var totalHeight = TotalHeight(frames);
                if (totalHeight >= maxRows)
                {
                    stopReason = ScrollingCaptureStopReason.MaxHeightReached;
                    ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                    break;
                }

                if (!CanRetainExtraRows(frames, rect.Width, rect.Height, options.MemoryCapBytes))
                {
                    stopReason = ScrollingCaptureStopReason.MemoryCapReached;
                    ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var frameRequest = request with { IncludeCursor = request.IncludeCursor && frames.Count == 0 };
                using var rawCapture = await CaptureScrollingFrameBitmapAsync(rect, frameRequest, options, cancellationToken).ConfigureAwait(false);
                RecordBackend(rawCapture, ref backendUsed, ref fallbackReason);
                var frame = CloneBitmap(rawCapture.Bitmap);
                var hashes = RowHashes(frame);

                if (frames.Count == 0)
                {
                    frames.Add(frame);
                    firstFrameHashes = hashes;
                    previousFrameHashes = hashes;
                    accumulatedHashes.AddRange(hashes);
                    ReportProgress(options, frames, rect.Width, maxRows);
                }
                else
                {
                    var stickyRows = ScrollingFramePlanner.DetectStickyHeaderRows(firstFrameHashes ?? [], hashes, options.StickyHeaderMaxPixels);
                    var newContentStart = ScrollingFramePlanner.FindNewContentStart(accumulatedHashes, hashes, stickyRows);

                    if (newContentStart is null)
                    {
                        frame.Dispose();
                        stopReason = ScrollingCaptureStopReason.AlignmentFailed;
                        ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                        break;
                    }

                    var noNewRows = newContentStart.Value >= frame.Height;
                    if (noNewRows)
                    {
                        frame.Dispose();
                    }
                    else
                    {
                        var top = Math.Clamp(newContentStart.Value, 0, frame.Height - 1);
                        var cropped = CropBitmap(frame, new Rectangle(0, top, frame.Width, frame.Height - top));
                        if (!CanRetainExtraRows(frames, rect.Width, cropped.Height, options.MemoryCapBytes))
                        {
                            cropped.Dispose();
                            frame.Dispose();
                            stopReason = ScrollingCaptureStopReason.MemoryCapReached;
                            ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                            break;
                        }

                        frames.Add(cropped);
                        accumulatedHashes.AddRange(hashes.Skip(top));
                        frame.Dispose();
                        bottomProbeCount = 0;
                        ReportProgress(options, frames, rect.Width, maxRows);
                    }

                    var repeatedFrame = previousFrameHashes is not null && ScrollingFramePlanner.RowsEqual(previousFrameHashes, hashes);
                    if (noNewRows || repeatedFrame)
                    {
                        bottomProbeCount++;
                    }

                    previousFrameHashes = hashes;
                }

                if (bottomProbeCount >= options.BottomProbeFrames)
                {
                    stopReason = ScrollingCaptureStopReason.BottomReached;
                    ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                    break;
                }

                (scrollDownOverride ?? ScrollDown)(rect, Math.Max(1, (int)Math.Floor(rect.Height * options.ScrollStepRatio)));
                if (!await WaitForScrollSettleAsync(rect, request, options, cancellationToken).ConfigureAwait(false))
                {
                    stopReason = ScrollingCaptureStopReason.Timeout;
                    ReportProgress(options, frames, rect.Width, maxRows, stopReason);
                    break;
                }
            }

            if (frames.Count == 0)
            {
                throw new InvalidOperationException("Scrolling capture did not produce a valid frame.");
            }

            ReportProgress(options, frames, rect.Width, maxRows, stopReason);
            return CreateScrollingResult(frames, rect, request, options, backendUsed, fallbackReason, stopReason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ReportProgress(options, frames, rect.Width, maxRows, ScrollingCaptureStopReason.Cancelled);
            if (frames.Count > 0)
            {
                return CreateScrollingResult(
                    frames,
                    rect,
                    request,
                    options,
                    backendUsed,
                    fallbackReason,
                    ScrollingCaptureStopReason.Cancelled);
            }

            throw;
        }
        catch
        {
            ReportProgress(options, frames, rect.Width, maxRows, ScrollingCaptureStopReason.CaptureFailed);
            throw;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private async Task<BackendCapture> CaptureRegionBitmapAsync(PixelRect rect, CaptureRequest request, CancellationToken cancellationToken)
    {
        ValidateRect(rect);
        if (captureRegionBitmapOverride is not null)
        {
            return await captureRegionBitmapOverride(rect, request, cancellationToken).ConfigureAwait(false);
        }

        if (request.BackendPreference == CaptureBackendPreference.Gdi)
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(rect, request.IncludeCursor), CaptureBackendUsed.Gdi, null);
        }

        var monitor = MonitorSelection.FromRect(rect);
        if (!monitor.Bounds.Contains(rect))
        {
            const string reason = "Capture rectangle spans multiple monitors; using GDI fallback.";
            if (request.BackendPreference == CaptureBackendPreference.WindowsGraphicsCapture)
            {
                throw new InvalidOperationException(reason);
            }

            return new BackendCapture(gdiFallback.CaptureScreenRect(rect, request.IncludeCursor), CaptureBackendUsed.Gdi, reason);
        }

        try
        {
            using var monitorBitmap = await graphicsCapture.CaptureMonitorAsync(monitor.Handle, request.IncludeCursor, cancellationToken).ConfigureAwait(false);
            var crop = rect.RelativeTo(monitor.Bounds);
            return new BackendCapture(
                CropBitmap(monitorBitmap, new Rectangle(crop.X, crop.Y, crop.Width, crop.Height)),
                CaptureBackendUsed.WindowsGraphicsCapture,
                null);
        }
        catch (Exception exception) when (CanUseGdiFallback(request, exception))
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(rect, request.IncludeCursor), CaptureBackendUsed.Gdi, FallbackReason(exception));
        }
    }

    private async Task<BackendCapture> CaptureScrollingFrameBitmapAsync(
        PixelRect rect,
        CaptureRequest request,
        ScrollingCaptureOptions options,
        CancellationToken cancellationToken)
    {
        if (options.BeforeScreenCapture is not null)
        {
            await options.BeforeScreenCapture(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await CaptureRegionBitmapAsync(rect, request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (options.AfterScreenCapture is not null)
            {
                await options.AfterScreenCapture(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<BackendCapture> CaptureWindowBitmapAsync(IntPtr hwnd, PixelRect rect, CaptureRequest request, CancellationToken cancellationToken)
    {
        ValidateRect(rect);
        if (request.BackendPreference == CaptureBackendPreference.Gdi)
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(rect, request.IncludeCursor), CaptureBackendUsed.Gdi, null);
        }

        try
        {
            return new BackendCapture(
                await graphicsCapture.CaptureWindowAsync(hwnd, request.IncludeCursor, cancellationToken).ConfigureAwait(false),
                CaptureBackendUsed.WindowsGraphicsCapture,
                null);
        }
        catch (Exception exception) when (CanUseGdiFallback(request, exception))
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(rect, request.IncludeCursor), CaptureBackendUsed.Gdi, FallbackReason(exception));
        }
    }

    private async Task<BackendCapture> CaptureMonitorBitmapAsync(MonitorInfo monitor, CaptureRequest request, CancellationToken cancellationToken)
    {
        if (request.BackendPreference == CaptureBackendPreference.Gdi)
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(monitor.Bounds, request.IncludeCursor), CaptureBackendUsed.Gdi, null);
        }

        try
        {
            return new BackendCapture(
                await graphicsCapture.CaptureMonitorAsync(monitor.Handle, request.IncludeCursor, cancellationToken).ConfigureAwait(false),
                CaptureBackendUsed.WindowsGraphicsCapture,
                null);
        }
        catch (Exception exception) when (CanUseGdiFallback(request, exception))
        {
            return new BackendCapture(gdiFallback.CaptureScreenRect(monitor.Bounds, request.IncludeCursor), CaptureBackendUsed.Gdi, FallbackReason(exception));
        }
    }

    private async Task<bool> WaitForScrollSettleAsync(PixelRect rect, CaptureRequest request, ScrollingCaptureOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<ulong>? previousProbe = null;
        var stableProbes = 0;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(options.SettleTimeoutMilliseconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(options.SettlePollMilliseconds, cancellationToken).ConfigureAwait(false);

            using var probe = await CaptureScrollingFrameBitmapAsync(rect, request, options, cancellationToken).ConfigureAwait(false);
            var hashes = RowHashes(probe.Bitmap);
            if (previousProbe is not null && ScrollingFramePlanner.RowsEqual(previousProbe, hashes))
            {
                stableProbes++;
                if (stableProbes >= options.RequiredStableSettleSamples)
                {
                    return true;
                }
            }
            else
            {
                stableProbes = 0;
            }

            previousProbe = hashes;
        }

        return false;
    }

    private static Task DelayIfNeeded(CaptureRequest request, CancellationToken cancellationToken) =>
        request.DelaySeconds > 0
            ? Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds), cancellationToken)
            : Task.CompletedTask;

    private static CaptureMetadata Metadata(CaptureMode mode, string? windowTitle, PixelRect? sourceRect, CaptureRequest request, BackendCapture capture, PixelRect? captureBounds = null) =>
        new(
            mode,
            windowTitle,
            sourceRect,
            DateTimeOffset.Now,
            request.BackendPreference,
            capture.BackendUsed,
            capture.FallbackReason,
            captureBounds ?? sourceRect);

    private static CaptureResult CreateResult(Bitmap bitmap, CaptureMetadata metadata) =>
        new(CopyBgraPixels(bitmap, metadata), metadata);

    private static CapturedPixels CopyBgraPixels(Bitmap bitmap, CaptureMetadata metadata)
    {
        using var normalized = CloneBitmap(bitmap);
        var stride = normalized.Width * 4;
        var pixels = new byte[stride * normalized.Height];
        var data = normalized.LockBits(new Rectangle(0, 0, normalized.Width, normalized.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < normalized.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * stride, stride);
            }
        }
        finally
        {
            normalized.UnlockBits(data);
        }

        return new CapturedPixels(pixels, normalized.Width, normalized.Height, stride, metadata);
    }

    private static bool CanUseGdiFallback(CaptureRequest request, Exception exception) =>
        request.BackendPreference != CaptureBackendPreference.WindowsGraphicsCapture
        && WindowsGraphicsCaptureBackend.CanFallback(exception);

    private static string FallbackReason(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";

    private static void RecordBackend(BackendCapture capture, ref CaptureBackendUsed backendUsed, ref string? fallbackReason)
    {
        if (capture.BackendUsed != CaptureBackendUsed.Gdi)
        {
            return;
        }

        backendUsed = CaptureBackendUsed.Gdi;
        fallbackReason ??= capture.FallbackReason;
    }

    private static void ReportProgress(
        ScrollingCaptureOptions options,
        IReadOnlyList<Bitmap> frames,
        int width,
        int maxRows,
        ScrollingCaptureStopReason? stopReason = null) =>
        options.Progress?.Report(
            new ScrollingCaptureProgress(
                frames.Count,
                Math.Min(TotalHeight(frames), maxRows),
                maxRows,
                EstimatedBytes(frames, width),
                false,
                stopReason));

    private static CaptureResult CreateScrollingResult(
        IReadOnlyList<Bitmap> frames,
        PixelRect rect,
        CaptureRequest request,
        ScrollingCaptureOptions options,
        CaptureBackendUsed backendUsed,
        string? fallbackReason,
        ScrollingCaptureStopReason stopReason)
    {
        using var stitched = StitchFrames(frames, rect.Width, Math.Min(options.MaxHeight, TotalHeight(frames)));
        var metadata = new CaptureMetadata(
            CaptureMode.Scrolling,
            null,
            rect,
            DateTimeOffset.Now,
            request.BackendPreference,
            backendUsed,
            fallbackReason,
            rect,
            StopReason: stopReason);
        return CreateResult(stitched, metadata);
    }

    private static bool CanRetainExtraRows(IReadOnlyList<Bitmap> frames, int width, int extraRows, int memoryCapBytes) =>
        frames.Count == 0 || EstimatedBytes(frames, width) + FrameBytes(width, extraRows) <= Math.Max(0, memoryCapBytes);

    private static long FrameBytes(int width, int height) =>
        (long)Math.Max(0, width) * Math.Max(0, height) * 4;

    private static void ValidateRect(PixelRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "Capture rectangle must have positive dimensions.");
        }
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

    private sealed record BackendCapture(Bitmap Bitmap, CaptureBackendUsed BackendUsed, string? FallbackReason) : IDisposable
    {
        public void Dispose() => Bitmap.Dispose();
    }
}
