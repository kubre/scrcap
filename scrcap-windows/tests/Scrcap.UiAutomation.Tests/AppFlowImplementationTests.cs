using System.IO;

namespace Scrcap.UiAutomation.Tests;

public sealed class AppFlowImplementationTests
{
    [Fact]
    public void AppRoutesWindowCaptureThroughWindowPicker()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("WindowTargetAsync", source, StringComparison.Ordinal);
        Assert.Contains("WindowFromPoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureWindow => new CaptureTarget(AppAction.CaptureWindow, null)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRoutesScrollingCaptureThroughScrollingService()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("CaptureScrollingRegionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ScrollingCaptureHud.ShowFor", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureRegion or AppAction.CaptureDelayed or AppAction.CaptureScrolling", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, relativePath));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Scrcap.Core")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate scrcap-windows root.");
    }
}
