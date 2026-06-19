using Scrcap.Core;

namespace Scrcap.Windows.Platform.Capture;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(PixelRect other) =>
        other.X >= X
        && other.Y >= Y
        && other.Right <= Right
        && other.Bottom <= Bottom;

    public PixelRect RelativeTo(PixelRect parent) =>
        new(X - parent.X, Y - parent.Y, Width, Height);
}

public readonly record struct PixelPoint(int X, int Y);

public sealed record WindowCandidate(
    IntPtr Hwnd,
    PixelRect Bounds,
    string Title);

public enum CaptureBackendPreference
{
    Auto,
    WindowsGraphicsCapture,
    Gdi,
}

public enum CaptureBackendUsed
{
    Unknown,
    WindowsGraphicsCapture,
    Gdi,
}

public sealed record CaptureMetadata(
    CaptureMode Mode,
    string? WindowTitle,
    PixelRect? SourceRect,
    DateTimeOffset CapturedAt,
    CaptureBackendPreference RequestedBackend = CaptureBackendPreference.Auto,
    CaptureBackendUsed BackendUsed = CaptureBackendUsed.Unknown,
    string? FallbackReason = null,
    PixelRect? CaptureBounds = null,
    double DpiScaleX = 1,
    double DpiScaleY = 1,
    ScrollingCaptureStopReason? StopReason = null)
{
    public PixelRect? EffectiveCaptureBounds => CaptureBounds ?? SourceRect;
}

public sealed record CapturedPixels(
    ReadOnlyMemory<byte> Bgra32,
    int PixelWidth,
    int PixelHeight,
    int Stride,
    CaptureMetadata Metadata)
{
    public int RequiredByteLength => Math.Max(0, PixelHeight) * Math.Abs(Stride);

    public void Validate()
    {
        if (PixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelWidth), "Pixel width must be positive.");
        }

        if (PixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PixelHeight), "Pixel height must be positive.");
        }

        if (Stride < PixelWidth * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(Stride), "Stride must fit one BGRA row.");
        }

        if (Bgra32.Length < RequiredByteLength)
        {
            throw new ArgumentException("Pixel buffer is shorter than the declared dimensions and stride.", nameof(Bgra32));
        }
    }
}

public sealed record CaptureResult(
    CapturedPixels Pixels,
    CaptureMetadata Metadata)
{
    public int PixelWidth => Pixels.PixelWidth;

    public int PixelHeight => Pixels.PixelHeight;
}

public interface IWindowsCaptureService
{
    Task<CaptureResult> CaptureRegionAsync(PixelRect rect, CaptureRequest request, CancellationToken cancellationToken);

    Task<CaptureResult> CaptureActiveWindowAsync(CaptureRequest request, CancellationToken cancellationToken);

    Task<CaptureResult> CaptureWindowAsync(IntPtr hwnd, CaptureRequest request, CancellationToken cancellationToken);

    Task<CaptureResult> CaptureMonitorUnderCursorAsync(CaptureRequest request, CancellationToken cancellationToken);

    Task<CaptureResult> CaptureScrollingRegionAsync(PixelRect rect, CaptureRequest request, ScrollingCaptureOptions options, CancellationToken cancellationToken);
}

public interface IWindowSelectionService
{
    WindowCandidate? WindowFromPoint(PixelPoint point);

    IReadOnlyList<WindowCandidate> EnumerateWindows();
}

public sealed record CaptureRequest(
    bool IncludeCursor,
    bool IncludeWindowShadow,
    bool WindowBackgroundTransparent,
    string WindowBackgroundHex,
    int DelaySeconds,
    CaptureBackendPreference BackendPreference = CaptureBackendPreference.Auto);

public sealed record ScrollingCaptureOptions(
    int MaxHeight,
    int MemoryCapBytes = 256 * 1024 * 1024,
    int SettlePollMilliseconds = 120,
    int SettleTimeoutMilliseconds = 600,
    int StickyHeaderMaxPixels = 160,
    int BottomProbeFrames = 2,
    double ScrollStepRatio = 0.72,
    int RequiredStableSettleSamples = 2,
    IProgress<ScrollingCaptureProgress>? Progress = null,
    Func<CancellationToken, Task>? BeforeScreenCapture = null,
    Func<CancellationToken, Task>? AfterScreenCapture = null);

public enum ScrollingCaptureStopReason
{
    Completed,
    BottomReached,
    Cancelled,
    Timeout,
    MaxHeightReached,
    MemoryCapReached,
    AlignmentFailed,
    CaptureFailed,
}

public sealed record ScrollingCaptureProgress(
    int FrameCount,
    int OutputHeight,
    int MaxPixelHeight,
    long EstimatedBytes,
    bool IsSpoolingToDisk,
    ScrollingCaptureStopReason? StopReason)
{
    public ScrollingCaptureProgress(int frameCount, int outputHeight, int maxPixelHeight)
        : this(frameCount, outputHeight, maxPixelHeight, 0, false, null)
    {
    }

    public int PixelHeight => OutputHeight;
}

internal sealed record MonitorInfo(
    IntPtr Handle,
    PixelRect Bounds,
    string DeviceName);
