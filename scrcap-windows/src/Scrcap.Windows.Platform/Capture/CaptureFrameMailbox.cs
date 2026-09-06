namespace Scrcap.Windows.Platform.Capture;

/// <summary>
/// Owns a single capture frame across the producer/async-consumer boundary.
/// Disposing closes admission before releasing the winner, so callbacks racing
/// cancellation cannot strand a frame in an abandoned TaskCompletionSource.
/// </summary>
internal sealed class CaptureFrameMailbox<T> : IDisposable where T : class, IDisposable
{
    private readonly TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int disposed;

    public Task<T> Task => completion.Task;

    public void Offer(T frame)
    {
        if (!completion.TrySetResult(frame))
        {
            frame.Dispose();
        }
    }

    public void Fail(Exception exception) => completion.TrySetException(exception);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        completion.TrySetCanceled();
        if (completion.Task.IsCompletedSuccessfully)
        {
            completion.Task.Result.Dispose();
        }
        else if (completion.Task.IsFaulted)
        {
            // A callback may fail after its only waiter has been cancelled.
            _ = completion.Task.Exception;
        }
    }
}
