using System.Drawing;
using System.Drawing.Imaging;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;

namespace Scrcap.Windows.Platform.Tests;

public sealed class CaptureLifetimeTests
{
    [Fact]
    public async Task MailboxKeepsWinnerAliveAndDisposesDuplicateAndLateFrames()
    {
        var mailbox = new CaptureFrameMailbox<TrackedFrame>();
        var winner = new TrackedFrame();
        var duplicate = new TrackedFrame();
        var late = new TrackedFrame();
        mailbox.Offer(winner);
        mailbox.Offer(duplicate);
        Assert.Same(winner, await mailbox.Task);
        Assert.Equal(0, winner.DisposeCount);
        Assert.Equal(1, duplicate.DisposeCount);
        mailbox.Dispose();
        mailbox.Offer(late);
        mailbox.Dispose();
        Assert.Equal(1, winner.DisposeCount);
        Assert.Equal(1, late.DisposeCount);
    }

    [Fact]
    public async Task ClosingMailboxWithoutFrameCancelsWaiterAndDisposesLateArrival()
    {
        var mailbox = new CaptureFrameMailbox<TrackedFrame>();
        mailbox.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await mailbox.Task);
        var late = new TrackedFrame();
        mailbox.Offer(late);
        Assert.Equal(1, late.DisposeCount);
    }

    [Fact]
    public void ArrivalRacingCloseDisposesEveryFrameExactlyOnce()
    {
        // Repetition explores interleavings, not a runtime capacity limit.
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var mailbox = new CaptureFrameMailbox<TrackedFrame>();
            var frames = Enumerable.Range(0, 16).Select(_ => new TrackedFrame()).ToArray();
            Parallel.Invoke(() => Parallel.ForEach(frames, mailbox.Offer), mailbox.Dispose);
            mailbox.Dispose();
            Assert.All(frames, frame => Assert.Equal(1, frame.DisposeCount));
        }
    }

    [Fact]
    public async Task CaptureRestoresHudWithUncancelledTokenAfterCancellationInHide()
    {
        using var cancellation = new CancellationTokenSource();
        var restored = false;
        var service = new WindowsCaptureService((_, _, _) => throw new Exception("Capture must not start."));
        var options = new ScrollingCaptureOptions(100,
            BeforeScreenCapture: token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            AfterScreenCapture: token =>
            {
                token.ThrowIfCancellationRequested();
                restored = true;
                return Task.CompletedTask;
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CaptureScrollingRegionAsync(
            new PixelRect(0, 0, 12, 8), Request, options, cancellation.Token));
        Assert.True(restored);
    }

    [Fact]
    public async Task FailedHudRestorationDisposesCapturedBitmap()
    {
        var bitmap = new Bitmap(12, 8, PixelFormat.Format32bppArgb);
        var service = new WindowsCaptureService((_, _, _) => Task.FromResult(bitmap));
        var expected = new InvalidOperationException("restore failed");
        var options = new ScrollingCaptureOptions(100,
            AfterScreenCapture: _ => throw expected);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureScrollingRegionAsync(
            new PixelRect(0, 0, 12, 8), Request, options, CancellationToken.None));
        Assert.Same(expected, error);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
    }

    [Fact]
    public void PixelValidationDoesNotOverflowRowOrBufferByteCount()
    {
        var metadata = new CaptureMetadata(CaptureMode.Region, null, null, DateTimeOffset.Now);
        var badRow = new CapturedPixels(ReadOnlyMemory<byte>.Empty, int.MaxValue, 1, 4, metadata);
        Assert.Throws<ArgumentOutOfRangeException>(badRow.Validate);
        var badBuffer = new CapturedPixels(ReadOnlyMemory<byte>.Empty, 1, int.MaxValue, 4, metadata);
        Assert.Equal(8589934588L, badBuffer.RequiredByteLength); // int.MaxValue rows * 4 bytes
        var error = Assert.Throws<ArgumentException>(badBuffer.Validate);
        Assert.Contains("required=8589934588", error.Message);
        Assert.Contains("actual=0", error.Message);
        var badStride = badBuffer with { Stride = int.MinValue };
        Assert.Throws<ArgumentOutOfRangeException>(badStride.Validate);
    }

    private static readonly CaptureRequest Request = new(false, false, false, "#FFFFFF", 0, CaptureBackendPreference.Gdi);

    private sealed class TrackedFrame : IDisposable
    {
        public int DisposeCount;
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }
}
