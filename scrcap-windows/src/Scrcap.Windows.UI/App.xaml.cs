using System.IO;
using System.Windows;
using System.Windows.Threading;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.Platform.Hotkeys;
using Scrcap.Windows.Platform.Tray;
using Scrcap.Windows.UI.Editor;
using Scrcap.Windows.UI.Onboarding;
using Scrcap.Windows.UI.Overlay;
using Scrcap.Windows.UI.Preferences;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.Windows.UI;

public partial class App : System.Windows.Application
{
    private IGlobalHotkeyService? hotkeys;
    private ITrayService? tray;
    private SettingsStore? settingsStore;
    private IWindowsCaptureService? captureService;
    private IWindowSelectionService? windowSelectionService;
    private CaptureTarget? lastCapture;
    private int captureFlowActive;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);
        ShutdownMode = options.TestMode ? ShutdownMode.OnLastWindowClose : ShutdownMode.OnExplicitShutdown;

        settingsStore = new SettingsStore(options.SettingsDirectory ?? SettingsStore.DefaultDirectory());
        if (options.AppTheme is { } appTheme)
        {
            settingsStore.Settings.ThemeMode = appTheme;
        }

        AppThemeService.Apply(Resources, settingsStore.Settings.ThemeMode);
        settingsStore.SettingsChanged += SettingsStore_SettingsChanged;

        if (!options.TestMode)
        {
            captureService = new WindowsCaptureService();
            windowSelectionService = new WindowSelectionService();
            hotkeys = new GlobalHotkeyService();
            RegisterHotkeys();
            hotkeys.Pressed += (_, action) => HandleCaptureAction(action);

            tray = new NotifyIconTrayService(new TaskbarThemeService(), () => settingsStore.Settings.Keymap);
            tray.CaptureRequested += (_, action) => HandleCaptureAction(action);
            tray.PreferencesRequested += (_, _) => OpenPreferences();
            tray.QuitRequested += (_, _) => Shutdown();
            tray.Show();
        }

        if (options.OpenPreferences)
        {
            OpenPreferences(options.DumpPath, options.TestMode);
            return;
        }

        if (options.OpenOnboarding)
        {
            OpenOnboarding(options.DumpPath, options.TestMode);
            return;
        }

        if (options.SampleEditorPath is { } samplePath)
        {
            OpenSampleEditor(samplePath, options.DumpPath, settingsStore.Settings, options.TestMode);
            return;
        }

        if (options.TestMode)
        {
            OpenSampleEditor(null, options.DumpPath, settingsStore.Settings, options.TestMode);
            return;
        }

        if (!settingsStore.Settings.HasShownFirstLaunchNotice)
        {
            settingsStore.Update(settings => settings.HasShownFirstLaunchNotice = true);
            OpenOnboarding();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (settingsStore is not null)
        {
            settingsStore.SettingsChanged -= SettingsStore_SettingsChanged;
        }

        tray?.Dispose();
        hotkeys?.Dispose();
        base.OnExit(e);
    }

    private async void HandleCaptureAction(AppAction action)
    {
        if (settingsStore is null || captureService is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref captureFlowActive, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var capture = await CaptureAsync(action, settingsStore.Settings, CancellationToken.None).ConfigureAwait(true);
            if (capture is null)
            {
                return;
            }

            var behaviorAction = action == AppAction.RepeatLast
                ? lastCapture?.Action ?? action
                : action;
            var behavior = BehaviorFor(behaviorAction, settingsStore.Settings);
            if (behavior is AfterCaptureBehavior.CopyOnly or AfterCaptureBehavior.Both)
            {
                var bitmap = EditorWindow.BitmapSourceFromPixels(capture.Pixels);
                var png = EditorClipboard.EncodePng(bitmap);
                System.Windows.Clipboard.SetDataObject(EditorClipboard.CreateDataObject(png), true);
                tray?.NotifyCopy(settingsStore.Settings);
            }

            if (behavior is AfterCaptureBehavior.OpenEditor or AfterCaptureBehavior.Both)
            {
                OpenEditor(capture);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            tray?.Notify("scrcap capture failed", ex.Message);
        }
        finally
        {
            Volatile.Write(ref captureFlowActive, 0);
        }
    }

    private async Task<CaptureResult?> CaptureAsync(AppAction action, Settings settings, CancellationToken cancellationToken)
    {
        var request = RequestFrom(settings, 0);

        if (action == AppAction.RepeatLast)
        {
            return lastCapture is null ? null : await CaptureTargetAsync(lastCapture, request, cancellationToken).ConfigureAwait(true);
        }

        CaptureTarget? target = action switch
        {
            AppAction.CaptureRegion => await RegionTargetAsync(AppAction.CaptureRegion, 0, cancellationToken).ConfigureAwait(true),
            AppAction.CaptureDelayed => await RegionTargetAsync(AppAction.CaptureDelayed, settings.CaptureDelaySeconds, cancellationToken).ConfigureAwait(true),
            AppAction.CaptureWindow => await WindowTargetAsync(settings.WindowCaptureTarget, cancellationToken).ConfigureAwait(true),
            AppAction.CaptureFullscreen => new CaptureTarget(AppAction.CaptureFullscreen, null),
            AppAction.CaptureScrolling => await RegionTargetAsync(AppAction.CaptureScrolling, 0, cancellationToken).ConfigureAwait(true),
            _ => null,
        };

        if (target is null)
        {
            return null;
        }

        var capture = await CaptureTargetAsync(target, request, cancellationToken).ConfigureAwait(true);
        lastCapture = RememberCaptureTarget(target, capture);
        return capture;
    }

    private async Task<CaptureTarget?> RegionTargetAsync(AppAction action, int countdownSeconds, CancellationToken cancellationToken)
    {
        var overlay = new OverlayWindow();
        try
        {
            var rect = await overlay.SelectRegionAsync(countdownSeconds).WaitAsync(cancellationToken).ConfigureAwait(true);
            return rect is null ? null : new CaptureTarget(action, rect.Value);
        }
        finally
        {
            overlay.Close();
            await WaitForOverlayDismissalAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<CaptureTarget?> WindowTargetAsync(WindowCaptureTarget captureTarget, CancellationToken cancellationToken)
    {
        if (captureTarget == WindowCaptureTarget.Active)
        {
            var activeWindow = windowSelectionService?.EnumerateWindows().FirstOrDefault();
            if (activeWindow is not null)
            {
                return new CaptureTarget(AppAction.CaptureWindow, activeWindow.Bounds, activeWindow.Hwnd);
            }
        }

        var overlay = new OverlayWindow();
        try
        {
            var candidate = await overlay.SelectWindowAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
            return candidate is null
                ? null
                : new CaptureTarget(AppAction.CaptureWindow, candidate.Bounds, candidate.Hwnd);
        }
        finally
        {
            overlay.Close();
            await WaitForOverlayDismissalAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<CaptureResult> CaptureTargetAsync(CaptureTarget target, CaptureRequest request, CancellationToken cancellationToken)
    {
        if (captureService is null)
        {
            throw new InvalidOperationException("Capture service is not initialized.");
        }

        if (target.Action == AppAction.CaptureScrolling && target.Region is { } scrollingRegion)
        {
            using var scrollingCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var hiddenWindows = HideAppWindowsForCapture();
            var hud = ScrollingCaptureHud.ShowFor(scrollingRegion, scrollingCancellation.Cancel);
            var progress = new Progress<ScrollingCaptureProgress>(hud.UpdateProgress);
            try
            {
                await WaitForOverlayDismissalAsync(scrollingCancellation.Token).ConfigureAwait(true);
                return await captureService.CaptureScrollingRegionAsync(
                    scrollingRegion,
                    request,
                    new ScrollingCaptureOptions(
                        settingsStore?.Settings.ScrollingMaxHeight ?? Settings.MaxScrollingMaxHeight,
                        Progress: progress,
                        BeforeScreenCapture: hud.HideForCaptureAsync,
                        AfterScreenCapture: hud.ShowAfterCaptureAsync),
                    scrollingCancellation.Token).ConfigureAwait(true);
            }
            finally
            {
                hud.Close();
            }
        }

        using (HideAppWindowsForCapture())
        {
            await WaitForOverlayDismissalAsync(cancellationToken).ConfigureAwait(true);
            return await (target.Action switch
            {
                AppAction.CaptureRegion or AppAction.CaptureDelayed
                    when target.Region is { } region => captureService.CaptureRegionAsync(region, request, cancellationToken),
                AppAction.CaptureWindow when target.WindowHandle is { } hwnd => captureService.CaptureWindowAsync(hwnd, request, cancellationToken),
                AppAction.CaptureFullscreen => CaptureFullscreenTargetAsync(target, request, cancellationToken),
                _ => throw new InvalidOperationException("No repeatable capture target is available."),
            }).ConfigureAwait(true);
        }
    }

    private async Task<CaptureResult> CaptureFullscreenTargetAsync(
        CaptureTarget target,
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (captureService is null)
        {
            throw new InvalidOperationException("Capture service is not initialized.");
        }

        if (target.Region is not { } monitorBounds)
        {
            return await captureService.CaptureMonitorUnderCursorAsync(request, cancellationToken).ConfigureAwait(true);
        }

        var regionCapture = await captureService.CaptureRegionAsync(monitorBounds, request, cancellationToken).ConfigureAwait(true);
        var metadata = regionCapture.Metadata with
        {
            Mode = CaptureMode.Fullscreen,
            WindowTitle = target.FullscreenMonitorName,
            SourceRect = monitorBounds,
            CaptureBounds = monitorBounds,
        };
        return new CaptureResult(regionCapture.Pixels with { Metadata = metadata }, metadata);
    }

    private static CaptureTarget RememberCaptureTarget(CaptureTarget target, CaptureResult capture) =>
        target.Action == AppAction.CaptureFullscreen && capture.Metadata.EffectiveCaptureBounds is { } monitorBounds
            ? target with
            {
                Region = monitorBounds,
                FullscreenMonitorName = capture.Metadata.WindowTitle,
            }
            : target;

    private async Task WaitForOverlayDismissalAsync(CancellationToken cancellationToken)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle, cancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(true);
    }

    private IDisposable HideAppWindowsForCapture()
    {
        var windows = Windows
            .OfType<Window>()
            .Where(window => window.IsVisible && window is not OverlayWindow and not ScrollingCaptureHud)
            .ToArray();
        foreach (var window in windows)
        {
            window.Hide();
        }

        return new Scope(() =>
        {
            foreach (var window in windows.Where(window => !window.IsVisible))
            {
                window.Show();
            }
        });
    }

    private static CaptureRequest RequestFrom(Settings settings, int delaySeconds) =>
        new(
            settings.IncludeCursor,
            settings.IncludeWindowShadow,
            settings.WindowBackgroundTransparent,
            settings.WindowBackgroundHex,
            delaySeconds);

    private static AfterCaptureBehavior BehaviorFor(AppAction action, Settings settings)
    {
        var mode = action switch
        {
            AppAction.CaptureWindow => CaptureMode.Window,
            AppAction.CaptureFullscreen => CaptureMode.Fullscreen,
            AppAction.CaptureScrolling => CaptureMode.Scrolling,
            _ => CaptureMode.Region,
        };

        return settings.BehaviorFor(mode);
    }

    private void OpenEditor(CaptureResult capture)
    {
        var window = new EditorWindow(
            capture,
            settingsStore?.Settings,
            () =>
            {
                if (tray is not null && settingsStore is not null)
                {
                    tray.NotifyCopy(settingsStore.Settings);
                }
            })
        {
            Title = capture.Metadata.WindowTitle is { Length: > 0 } title ? $"scrcap - {title}" : "scrcap",
        };
        window.Show();
        window.Activate();
    }

    private void OpenPreferences(string? dumpPath = null, bool shutdownAfterDump = false)
    {
        var hotkeySuspension = hotkeys?.Suspend();
        var window = new PreferencesWindow(settingsStore ?? new SettingsStore(SettingsStore.DefaultDirectory()));
        window.Closed += (_, _) =>
        {
            hotkeySuspension?.Dispose();
            RegisterHotkeys();
        };
        AttachDumpHook(window, dumpPath, shutdownAfterDump);
        window.Show();
        window.Activate();
    }

    private void OpenOnboarding(string? dumpPath = null, bool shutdownAfterDump = false)
    {
        var regionShortcut = settingsStore?.Settings.Keymap.ChordFor(AppAction.CaptureRegion)?.WindowsDisplayValue ?? "Alt+Shift+1";
        var window = new OnboardingWindow(
            regionShortcut,
            () => OpenPreferences());
        AttachDumpHook(window, dumpPath, shutdownAfterDump);
        window.Show();
        window.Activate();
    }

    private void RegisterHotkeys()
    {
        if (hotkeys is null || settingsStore is null)
        {
            return;
        }

        hotkeys.Register(settingsStore.Settings.Keymap);
        if (hotkeys.Failed.Count > 0)
        {
            tray?.Notify("scrcap hotkey warning", $"{hotkeys.Failed.Count} shortcut(s) could not be registered.");
        }
    }

    private static void OpenSampleEditor(string? samplePath, string? dumpPath, Settings settings, bool shutdownAfterDump)
    {
        var window = samplePath is { Length: > 0 }
            ? OpenSampleCapture(samplePath, settings)
            : new EditorWindow(settings: settings)
            {
                Title = "scrcap",
            };
        AttachDumpHook(window, dumpPath, shutdownAfterDump);
        window.Show();
        window.Activate();
    }

    private void SettingsStore_SettingsChanged(object? sender, EventArgs e)
    {
        if (settingsStore is null)
        {
            return;
        }

        AppThemeService.Apply(Resources, settingsStore.Settings.ThemeMode);
        foreach (var editor in Windows.OfType<EditorWindow>())
        {
            editor.ApplySettings(settingsStore.Settings);
        }

        if (!Windows.OfType<PreferencesWindow>().Any(window => window.IsVisible))
        {
            RegisterHotkeys();
        }
    }

    private static EditorWindow OpenSampleCapture(string samplePath, Settings settings)
    {
        var bytes = File.ReadAllBytes(samplePath);
        var image = EditorWindow.DecodePng(bytes);
        var metadata = new CaptureMetadata(CaptureMode.Region, Path.GetFileName(samplePath), null, DateTimeOffset.Now);
        return new EditorWindow(new CaptureResult(
            EditorWindow.CapturedPixelsFromBitmapSource(image, metadata),
            metadata),
            settings)
        {
            Title = "scrcap",
        };
    }

    private static string? TryGetOptionValue(IReadOnlyList<string> args, string option)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void AttachDumpHook(Window window, string? dumpPath, bool shutdownAfterDump)
    {
        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            return;
        }

        window.Loaded += (_, _) =>
        {
            window.Dispatcher.InvokeAsync(() =>
            {
                var width = Math.Max(1, (int)Math.Round(window.ActualWidth));
                var height = Math.Max(1, (int)Math.Round(window.ActualHeight));
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dumpPath))!);
                using var stream = File.Create(dumpPath);
                encoder.Save(stream);
                if (shutdownAfterDump)
                {
                    window.Close();
                }
            });
        };
    }

    private sealed record CaptureTarget(
        AppAction Action,
        PixelRect? Region,
        IntPtr? WindowHandle = null,
        string? FullscreenMonitorName = null);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            dispose();
        }
    }

    private sealed record StartupOptions(
        bool TestMode,
        string? DumpPath,
        string? SettingsDirectory,
        ThemeMode? AppTheme,
        bool OpenPreferences,
        bool OpenOnboarding,
        string? SampleEditorPath)
    {
        public static StartupOptions Parse(IReadOnlyList<string> args) =>
            new(
                args.Contains("--test-mode", StringComparer.OrdinalIgnoreCase),
                TryGetOptionValue(args, "--dump-window-png"),
                TryGetOptionValue(args, "--test-settings-dir"),
                TryGetThemeMode(TryGetOptionValue(args, "--test-app-theme")),
                args.Contains("--open-preferences", StringComparer.OrdinalIgnoreCase),
                args.Contains("--open-onboarding", StringComparer.OrdinalIgnoreCase),
                TryGetOptionValue(args, "--open-sample-editor"));

        private static ThemeMode? TryGetThemeMode(string? value) =>
            value?.ToLowerInvariant() switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                "system" => ThemeMode.System,
                _ => null,
            };
    }
}
