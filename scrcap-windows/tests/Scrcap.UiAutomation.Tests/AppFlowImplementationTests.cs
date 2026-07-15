using System.IO;

namespace Scrcap.UiAutomation.Tests;

public sealed class AppFlowImplementationTests
{
    [Fact]
    public void AppRoutesWindowCaptureThroughWindowPicker()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("WindowTargetAsync", source, StringComparison.Ordinal);
        Assert.Contains("settings.WindowCaptureTarget", source, StringComparison.Ordinal);
        Assert.Contains("WindowCaptureTarget.Active", source, StringComparison.Ordinal);
        Assert.Contains("windowSelectionService?.EnumerateWindows().FirstOrDefault()", source, StringComparison.Ordinal);
        Assert.Contains("SelectWindowAsync", source, StringComparison.Ordinal);
        Assert.Contains("CaptureWindowAsync(hwnd", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureWindow => new CaptureTarget(AppAction.CaptureWindow, null)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppAllowsOnlyOneCaptureFlowAtATime()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("Interlocked.CompareExchange(ref captureFlowActive, 1, 0)", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("Volatile.Write(ref captureFlowActive, 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppAppliesLiveSettingsAndPublishesRealPngClipboardData()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("AppThemeService.Apply(Resources, settingsStore.Settings.ThemeMode)", source, StringComparison.Ordinal);
        Assert.Contains("settingsStore.SettingsChanged += SettingsStore_SettingsChanged", source, StringComparison.Ordinal);
        Assert.Contains("editor.ApplySettings(settingsStore.Settings)", source, StringComparison.Ordinal);
        Assert.Contains("EditorClipboard.EncodePng(bitmap)", source, StringComparison.Ordinal);
        Assert.Contains("Clipboard.SetDataObject(EditorClipboard.CreateDataObject(png), true)", source, StringComparison.Ordinal);
        Assert.Contains("tray?.NotifyCopy(settingsStore.Settings)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeBrushConsumersRemainLiveAcrossOpenWindows()
    {
        var uiRoot = Path.Combine(FindRepoRoot(), "src", "Scrcap.Windows.UI");
        foreach (var path in Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.EndsWith("ThemeTokens.xaml", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.DoesNotContain("{StaticResource Brush", File.ReadAllText(path), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RepeatFullscreenUsesTheOriginalMonitorBoundsAndPreservesFullscreenMetadata()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("RememberCaptureTarget", source, StringComparison.Ordinal);
        Assert.Contains("capture.Metadata.EffectiveCaptureBounds", source, StringComparison.Ordinal);
        Assert.Contains("CaptureRegionAsync(monitorBounds", source, StringComparison.Ordinal);
        Assert.Contains("Mode = CaptureMode.Fullscreen", source, StringComparison.Ordinal);
        Assert.Contains("behaviorAction = action == AppAction.RepeatLast", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRoutesScrollingCaptureThroughScrollingService()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("CaptureScrollingRegionAsync", source, StringComparison.Ordinal);
        Assert.Contains("ScrollingCaptureHud.ShowFor", source, StringComparison.Ordinal);
        Assert.Contains("CancellationTokenSource.CreateLinkedTokenSource", source, StringComparison.Ordinal);
        Assert.Contains("BeforeScreenCapture: hud.HideForCaptureAsync", source, StringComparison.Ordinal);
        Assert.Contains("AfterScreenCapture: hud.ShowAfterCaptureAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppAction.CaptureRegion or AppAction.CaptureDelayed or AppAction.CaptureScrolling", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppClosesSelectionOverlaysAfterUse()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("finally", source, StringComparison.Ordinal);
        Assert.Contains("overlay.Close()", source, StringComparison.Ordinal);
        Assert.Contains("WaitForOverlayDismissalAsync", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppHidesOwnWindowsAroundCapture()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");

        Assert.Contains("HideAppWindowsForCapture", source, StringComparison.Ordinal);
        Assert.Contains("window is not OverlayWindow and not ScrollingCaptureHud", source, StringComparison.Ordinal);
        Assert.Contains("window.Hide()", source, StringComparison.Ordinal);
        Assert.Contains("window.Show()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppShowsOnboardingOnlyOnFirstNormalLaunch()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/App.xaml.cs");
        var settings = ReadRepoFile("src/Scrcap.Core/Settings.cs");
        var onboarding = ReadRepoFile("src/Scrcap.Windows.UI/Onboarding/OnboardingWindow.xaml");

        Assert.Contains("HasShownFirstLaunchNotice", settings, StringComparison.Ordinal);
        Assert.Contains("!settingsStore.Settings.HasShownFirstLaunchNotice", source, StringComparison.Ordinal);
        Assert.Contains("OpenOnboarding", source, StringComparison.Ordinal);
        Assert.Contains("--open-onboarding", source, StringComparison.Ordinal);
        Assert.Contains("Welcome to scrcap", onboarding, StringComparison.Ordinal);
        Assert.Contains("It lives in your system tray", onboarding, StringComparison.Ordinal);
        Assert.Contains("Grab a region in one press", onboarding, StringComparison.Ordinal);
        Assert.Contains("Mark up, then copy or save", onboarding, StringComparison.Ordinal);
        Assert.Contains("captures only when you use a shortcut or the tray menu", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("Open Privacy Settings", onboarding, StringComparison.Ordinal);
        Assert.DoesNotContain("ms-settings:privacy-graphicsCaptureProgrammatic", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollingHudHideWaitsForRenderIdleBeforeCapture()
    {
        var source = ReadRepoFile("src/Scrcap.Windows.UI/Overlay/ScrollingCaptureHud.xaml.cs");

        Assert.Contains("HideForCaptureAsync", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Render", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(50)", source, StringComparison.Ordinal);
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
