using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
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

    [Fact]
    public async Task DeterministicCaptureFixtureExposesHwndAndCapturesColoredCorners()
    {
        using var fixture = StartFixture();
        var hwnd = fixture.WaitForValue("SCRCAP_FIXTURE_HWND");
        var service = new WindowsCaptureService();

        var result = await service.CaptureWindowAsync(
            new IntPtr(long.Parse(hwnd)),
            new CaptureRequest(false, false, true, "#FFFFFF", 0, CaptureBackendPreference.Gdi),
            CancellationToken.None);

        Assert.Equal(CaptureBackendUsed.Gdi, result.Metadata.BackendUsed);
        Assert.Equal(240, result.PixelWidth);
        Assert.Equal(180, result.PixelHeight);
        Assert.Equal(Color.Red.ToArgb(), PixelAt(result, 4, 4).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), PixelAt(result, result.PixelWidth - 5, 4).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), PixelAt(result, 4, result.PixelHeight - 5).ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), PixelAt(result, result.PixelWidth - 5, result.PixelHeight - 5).ToArgb());
        Assert.DoesNotContain(ReadPixels(result), color => color.ToArgb() == Color.Magenta.ToArgb());
    }

    [Fact]
    public void DeterministicScrollFixtureExposesScrollablePanelHwnd()
    {
        using var fixture = StartFixture("--scroll-fixture");

        Assert.True(long.Parse(fixture.WaitForValue("SCRCAP_FIXTURE_HWND")) != 0);
        Assert.True(long.Parse(fixture.WaitForValue("SCRCAP_SCROLL_PANEL_HWND")) != 0);
        Assert.Equal("24", fixture.WaitForValue("SCRCAP_SCROLL_ROWS"));
    }

    private static Bitmap CreateSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private static FixtureProcess StartFixture(params string[] args)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CaptureFixture", "Scrcap.CaptureFixture.exe");
        Assert.True(File.Exists(path), $"Capture fixture executable was not copied to {path}.");
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return new FixtureProcess(Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start capture fixture."));
    }

    private static Color PixelAt(CaptureResult result, int x, int y)
    {
        var index = (y * result.Pixels.Stride) + (x * 4);
        var bytes = result.Pixels.Bgra32.Span;
        return Color.FromArgb(bytes[index + 3], bytes[index + 2], bytes[index + 1], bytes[index]);
    }

    private static IReadOnlyList<Color> ReadPixels(CaptureResult result)
    {
        var colors = new List<Color>();
        for (var y = 0; y < result.PixelHeight; y++)
        {
            for (var x = 0; x < result.PixelWidth; x++)
            {
                colors.Add(PixelAt(result, x, y));
            }
        }

        return colors;
    }

    private sealed class FixtureProcess : IDisposable
    {
        private readonly Process process;
        private readonly Dictionary<string, string> values = [];
        private readonly AutoResetEvent lineReceived = new(false);
        private readonly List<string> stderr = [];

        public FixtureProcess(Process process)
        {
            this.process = process;
            process.OutputDataReceived += (_, args) =>
            {
                if (args.Data is null)
                {
                    lineReceived.Set();
                    return;
                }

                var split = args.Data.Split('=', 2);
                if (split.Length == 2)
                {
                    lock (values)
                    {
                        values[split[0]] = split[1];
                    }
                }

                lineReceived.Set();
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data is not null)
                {
                    lock (stderr)
                    {
                        stderr.Add(args.Data);
                    }
                }
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public string WaitForValue(string key)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                lock (values)
                {
                    if (values.TryGetValue(key, out var value))
                    {
                        return value;
                    }
                }

                if (process.HasExited)
                {
                    break;
                }

                var remaining = deadline - DateTime.UtcNow;
                lineReceived.WaitOne(remaining > TimeSpan.Zero && remaining < TimeSpan.FromMilliseconds(250)
                    ? remaining
                    : TimeSpan.FromMilliseconds(250));
            }

            string errorText;
            lock (stderr)
            {
                errorText = string.Join(Environment.NewLine, stderr);
            }

            throw new TimeoutException($"Fixture did not report {key}. stderr: {errorText}");
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }
    }
}
