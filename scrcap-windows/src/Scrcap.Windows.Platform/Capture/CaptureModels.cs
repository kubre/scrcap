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

public sealed record CaptureMetadata(
    CaptureMode Mode,
    string? WindowTitle,
    PixelRect? SourceRect,
    DateTimeOffset CapturedAt);

public sealed record CaptureResult(
    byte[] PngBytes,
    int PixelWidth,
    int PixelHeight,
    CaptureMetadata Metadata);

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
    int DelaySeconds);

public sealed record ScrollingCaptureOptions(
    int MaxHeight,
    int MemoryCapBytes = 256 * 1024 * 1024,
    int SettlePollMilliseconds = 120,
    int SettleTimeoutMilliseconds = 600,
    int StickyHeaderMaxPixels = 160,
    int BottomProbeFrames = 2,
    double ScrollStepRatio = 0.72,
    int RequiredStableSettleSamples = 2,
    IProgress<ScrollingCaptureProgress>? Progress = null);

public sealed record ScrollingCaptureProgress(
    int FrameCount,
    int PixelHeight,
    int MaxPixelHeight);

internal sealed record MonitorInfo(
    IntPtr Handle,
    PixelRect Bounds,
    string DeviceName);
