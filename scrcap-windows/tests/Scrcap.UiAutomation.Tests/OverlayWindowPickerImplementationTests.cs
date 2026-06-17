using System.IO;

namespace Scrcap.UiAutomation.Tests;

public sealed class OverlayWindowPickerImplementationTests
{
    [Fact]
    public void OverlayIncludesWindowPickerInteractions()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("IWindowSelectionService", source, StringComparison.Ordinal);
        Assert.Contains("WindowFromPoint", source, StringComparison.Ordinal);
        Assert.Contains("EnumerateWindows", source, StringComparison.Ordinal);
        Assert.Contains("CycleWindowHighlight", source, StringComparison.Ordinal);
        Assert.Contains("Key.Tab", source, StringComparison.Ordinal);
        Assert.Contains("Key.Enter", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayIncludesPlanRequiredVisualStates()
    {
        var xaml = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml");
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/OverlayWindow.xaml.cs");

        Assert.Contains("MonitorLayer", xaml, StringComparison.Ordinal);
        Assert.Contains("WindowHighlight", xaml, StringComparison.Ordinal);
        Assert.Contains("HintTag", xaml, StringComparison.Ordinal);
        Assert.Contains("OverlapTag", xaml, StringComparison.Ordinal);
        Assert.Contains("CountdownOverlay", xaml, StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleAnimation", xaml, StringComparison.Ordinal);
        Assert.Contains("RenderMonitorLayer", source, StringComparison.Ordinal);
        Assert.Contains("CountIntersectingMonitors", source, StringComparison.Ordinal);
        Assert.Contains("ShowCountdownAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRoutesDelayedCaptureCountdownThroughOverlay()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("settings.CaptureDelaySeconds", source, StringComparison.Ordinal);
        Assert.Contains("SelectRegionAsync(countdownSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("RequestFrom(settings, 0)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestFrom(settings, action == AppAction.CaptureDelayed", source, StringComparison.Ordinal);
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
