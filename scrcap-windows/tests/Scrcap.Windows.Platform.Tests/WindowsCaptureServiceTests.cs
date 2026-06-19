using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;

namespace Scrcap.Windows.Platform.Tests;

public sealed class WindowsCaptureServiceTests
{
    [Fact]
    public async Task ScrollingCaptureCancellationAfterFirstFrameReturnsPartialResult()
    {
        using var cancellation = new CancellationTokenSource();
        var captureCount = 0;
        var visibilityEvents = new List<string>();
        var service = new WindowsCaptureService(
            (rect, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                captureCount++;
                return Task.FromResult(CreateSolidBitmap(rect.Width, rect.Height, Color.Red));
            },
            (_, _) => cancellation.Cancel());

        var result = await service.CaptureScrollingRegionAsync(
            new PixelRect(0, 0, 12, 8),
            new CaptureRequest(false, false, false, "#FFFFFF", 0, CaptureBackendPreference.Gdi),
            new ScrollingCaptureOptions(
                MaxHeight: 200,
                SettlePollMilliseconds: 1,
                SettleTimeoutMilliseconds: 25,
                RequiredStableSettleSamples: 1,
                BeforeScreenCapture: _ =>
                {
                    visibilityEvents.Add("hide");
                    return Task.CompletedTask;
                },
                AfterScreenCapture: _ =>
                {
                    visibilityEvents.Add("show");
                    return Task.CompletedTask;
                }),
            cancellation.Token);

        Assert.Equal(1, captureCount);
        Assert.Equal(12, result.PixelWidth);
        Assert.Equal(8, result.PixelHeight);
        Assert.Equal(ScrollingCaptureStopReason.Cancelled, result.Metadata.StopReason);
        Assert.Equal(new[] { "hide", "show" }, visibilityEvents);
    }

    [Fact]
    public void WindowSelectionDetectsCurrentProcessWindows()
    {
        using var form = new Form();

        Assert.True(WindowSelectionService.IsOwnProcessWindow(form.Handle));
    }

    private static Bitmap CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }
}
