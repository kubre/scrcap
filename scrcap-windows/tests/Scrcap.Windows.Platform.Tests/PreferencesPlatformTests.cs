using System.Net;
using System.Text;
using Scrcap.Core;
using Scrcap.Windows.Platform.Startup;
using Scrcap.Windows.Platform.Tray;
using Scrcap.Windows.Platform.Updates;

namespace Scrcap.Windows.Platform.Tests;

public sealed class PreferencesPlatformTests
{
    [Fact]
    public void StartupFolderServiceCreatesAndRemovesPerUserLaunchScript()
    {
        using var temp = new TempDirectory();
        var executable = Path.Combine(temp.Path, "scrcap test.exe");
        File.WriteAllBytes(executable, []);
        var service = new StartupFolderLaunchAtLoginService(temp.Path);

        var enabled = service.SetEnabled(true, executable);

        Assert.True(enabled.Succeeded, enabled.ErrorMessage);
        Assert.True(service.IsEnabled());
        var script = File.ReadAllText(Path.Combine(temp.Path, "scrcap.cmd"));
        Assert.Contains("\"" + executable + "\"", script, StringComparison.Ordinal);
        Assert.Contains("--launch-at-login", script, StringComparison.Ordinal);

        var disabled = service.SetEnabled(false);
        Assert.True(disabled.Succeeded, disabled.ErrorMessage);
        Assert.False(service.IsEnabled());
    }

    [Theory]
    [InlineData("1.0", "v1.0.1", true)]
    [InlineData("1.0.1", "v1.0.1", false)]
    [InlineData("2.1.0+local", "2.1", false)]
    [InlineData("2.1-beta", "2.2", true)]
    public void ReleaseVersionsCompareNumericComponents(string currentRaw, string latestRaw, bool updateAvailable)
    {
        Assert.True(ReleaseVersion.TryParse(currentRaw, out var current));
        Assert.True(ReleaseVersion.TryParse(latestRaw, out var latest));
        Assert.Equal(updateAvailable, latest > current);
    }

    [Fact]
    public async Task UpdateCheckerUsesGitHubReleaseApiContract()
    {
        var handler = new StubHttpHandler(request =>
        {
            Assert.Equal("https://api.github.com/repos/kubre/scrcap/releases/latest", request.RequestUri?.AbsoluteUri);
            Assert.Contains(request.Headers.UserAgent, value => value.Product?.Name == "scrcap-windows");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"tag_name\":\"v2.0.0\",\"name\":\"scrcap 2\",\"html_url\":\"https://github.com/kubre/scrcap/releases/tag/v2.0.0\"}",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        var checker = new GitHubUpdateChecker(new HttpClient(handler));

        var result = await checker.CheckAsync("1.2.0");

        Assert.Equal(UpdateAvailability.Available, result.Availability);
        Assert.Equal("v2.0.0", result.Release.TagName);
    }

    [Fact]
    public void CopyNotificationContractHonorsSuppressionPreference()
    {
        var tray = new RecordingTrayService();
        var settings = Settings.Defaults();

        tray.NotifyCopy(settings);
        settings.SuppressCopyNotification = true;
        tray.NotifyCopy(settings);

        Assert.Single(tray.Notifications);
        Assert.Equal("Copied to clipboard", tray.Notifications[0].Title);
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }

    private sealed class RecordingTrayService : ITrayService
    {
        public event EventHandler<AppAction>? CaptureRequested { add { } remove { } }

        public event EventHandler? PreferencesRequested { add { } remove { } }

        public event EventHandler? QuitRequested { add { } remove { } }

        public string CurrentIconKey => "test";

        public List<(string Title, string Message)> Notifications { get; } = [];

        public void Show()
        {
        }

        public void Notify(string title, string message) => Notifications.Add((title, message));

        public void Dispose()
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-platform-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
