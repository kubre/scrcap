using System.IO;
using System.Windows;
using System.Windows.Threading;
using Scrcap.Core;
using Scrcap.Core.Diagnostics;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.Platform.Hotkeys;
using Scrcap.Windows.Platform.Startup;
using Scrcap.Windows.Platform.Tray;
using Scrcap.Windows.UI.Editor;
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
    private CaptureTarget? lastCapture;

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

        settingsStore.SettingsChanged += SettingsStore_SettingsChanged;
        AppThemeService.Apply(Resources, settingsStore.Settings.ThemeMode);

        if (!options.TestMode)
        {
            captureService = new WindowsCaptureService();
            hotkeys = new GlobalHotkeyService();
            RegisterHotkeys();
            hotkeys.Pressed += (_, action) => HandleCaptureAction(action);

            tray = new NotifyIconTrayService(new TaskbarThemeService());
            tray.CaptureRequested += (_, action) => HandleCaptureAction(action);
            tray.PreferencesRequested += (_, _) => OpenPreferences();
            tray.QuitRequested += (_, _) => Shutdown();
            tray.Show();
        }

        if (e.Args.Contains("--open-preferences", StringComparer.OrdinalIgnoreCase))
        {
            OpenPreferences(options.DumpPath, options.TestMode, options.PreferencesTabIndex);
            return;
        }

        if (TryGetOptionValue(e.Args, "--open-sample-editor") is { } samplePath)
        {
            OpenSampleEditor(samplePath, options.DumpPath, settingsStore.Settings, options.TestMode);
            return;
        }

        if (options.TestMode)
        {
            OpenSampleEditor(null, options.DumpPath, settingsStore.Settings, options.TestMode);
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

    private void SettingsStore_SettingsChanged(object? sender, Settings settings)
    {
        AppThemeService.Apply(Resources, settings.ThemeMode);
        RegisterHotkeys();
        foreach (var editorWindow in Windows.OfType<EditorWindow>())
        {
            editorWindow.ApplySettings(settings);
        }
    }

    private async void HandleCaptureAction(AppAction action)
    {
        if (settingsStore is null || captureService is null)
        {
            return;
        }

        var overlayFirstFrameSpan = UsesSelectionOverlay(action)
            ? ScrcapDiagnostics.Start("hotkey_received_to_overlay_first_frame", ("action", action))
            : null;
        try
        {
            var capture = await CaptureAsync(action, settingsStore.Settings, CancellationToken.None, overlayFirstFrameSpan).ConfigureAwait(true);
            if (capture is null)
            {
                overlayFirstFrameSpan?.Dispose();
                return;
            }

            var behavior = BehaviorFor(action, settingsStore.Settings);
            if (behavior is AfterCaptureBehavior.CopyOnly or AfterCaptureBehavior.Both)
            {
                System.Windows.Clipboard.SetImage(EditorWindow.BitmapSourceFromPixels(capture.Pixels));
                tray?.Notify("scrcap copied", "Capture copied to the clipboard.");
            }

            if (behavior is AfterCaptureBehavior.OpenEditor or AfterCaptureBehavior.Both)
            {
                var editorFirstInteractiveSpan = ScrcapDiagnostics.Start(
                    "capture_result_to_editor_first_interactive_render",
                    ("mode", capture.Metadata.Mode),
                    ("backend", capture.Metadata.BackendUsed),
                    ("pixelWidth", capture.PixelWidth),
                    ("pixelHeight", capture.PixelHeight));
                OpenEditor(capture, editorFirstInteractiveSpan);
            }
        }
        catch (OperationCanceledException)
        {
            overlayFirstFrameSpan?.Dispose();
        }
        catch (Exception ex)
        {
            overlayFirstFrameSpan?.Dispose();
            tray?.Notify("scrcap capture failed", ex.Message);
        }
    }

    private async Task<CaptureResult?> CaptureAsync(
        AppAction action,
        Settings settings,
        CancellationToken cancellationToken,
        DiagnosticSpan? overlayFirstFrameSpan)
    {
        var request = RequestFrom(settings, 0);

        if (action == AppAction.RepeatLast)
        {
            return lastCapture is null ? null : await CaptureTargetAsync(lastCapture, request, cancellationToken).ConfigureAwait(true);
        }

        CaptureTarget? target = action switch
        {
            AppAction.CaptureRegion => await RegionTargetAsync(AppAction.CaptureRegion, 0, cancellationToken, overlayFirstFrameSpan).ConfigureAwait(true),
            AppAction.CaptureDelayed => await RegionTargetAsync(AppAction.CaptureDelayed, settings.CaptureDelaySeconds, cancellationToken, overlayFirstFrameSpan).ConfigureAwait(true),
            AppAction.CaptureWindow => await WindowTargetAsync(cancellationToken, overlayFirstFrameSpan).ConfigureAwait(true),
            AppAction.CaptureFullscreen => new CaptureTarget(AppAction.CaptureFullscreen, null),
            AppAction.CaptureScrolling => await RegionTargetAsync(AppAction.CaptureScrolling, 0, cancellationToken, overlayFirstFrameSpan).ConfigureAwait(true),
            _ => null,
        };

        if (target is null)
        {
            return null;
        }

        lastCapture = target;
        using var captureSpan = ScrcapDiagnostics.Start(
            "selection_committed_to_capture_result",
            ("action", target.Action),
            ("requestedBackend", request.BackendPreference));
        var result = await CaptureTargetAsync(target, request, cancellationToken).ConfigureAwait(true);
        ScrcapDiagnostics.Mark(
            "capture_result",
            ("action", target.Action),
            ("backend", result.Metadata.BackendUsed),
            ("fallbackReason", result.Metadata.FallbackReason),
            ("captureBounds", result.Metadata.EffectiveCaptureBounds),
            ("dpiScaleX", result.Metadata.DpiScaleX),
            ("dpiScaleY", result.Metadata.DpiScaleY),
            ("pixelWidth", result.PixelWidth),
            ("pixelHeight", result.PixelHeight),
            ("stopReason", result.Metadata.StopReason));
        return result;
    }

    private async Task<CaptureTarget?> RegionTargetAsync(
        AppAction action,
        int countdownSeconds,
        CancellationToken cancellationToken,
        DiagnosticSpan? overlayFirstFrameSpan)
    {
        var overlay = new OverlayWindow();
        AttachFirstFrameSpan(overlay, overlayFirstFrameSpan);
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

    private async Task<CaptureTarget?> WindowTargetAsync(CancellationToken cancellationToken, DiagnosticSpan? overlayFirstFrameSpan)
    {
        var overlay = new OverlayWindow();
        AttachFirstFrameSpan(overlay, overlayFirstFrameSpan);
        try
        {
            var candidate = await overlay.SelectWindowAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
            if (candidate is null)
            {
                return null;
            }

            return new CaptureTarget(AppAction.CaptureWindow, candidate.Bounds, candidate.Hwnd);
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
                AppAction.CaptureFullscreen => captureService.CaptureMonitorUnderCursorAsync(request, cancellationToken),
                _ => throw new InvalidOperationException("No repeatable capture target is available."),
            }).ConfigureAwait(true);
        }
    }

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

    private void OpenEditor(CaptureResult capture, DiagnosticSpan? firstInteractiveRenderSpan = null)
    {
        var window = new EditorWindow(capture, settingsStore?.Settings, firstInteractiveRenderSpan)
        {
            Title = capture.Metadata.WindowTitle is { Length: > 0 } title ? $"scrcap - {title}" : "scrcap",
        };
        window.Show();
        window.Activate();
    }

    private static void AttachFirstFrameSpan(Window window, DiagnosticSpan? span)
    {
        if (span is null)
        {
            return;
        }

        window.ContentRendered += (_, _) => span.Dispose();
        window.Closed += (_, _) => span.Dispose();
    }

    private static bool UsesSelectionOverlay(AppAction action) =>
        action is AppAction.CaptureRegion
            or AppAction.CaptureDelayed
            or AppAction.CaptureWindow
            or AppAction.CaptureScrolling;

    private void OpenPreferences(string? dumpPath = null, bool shutdownAfterDump = false, int preferencesTabIndex = 0)
    {
        var window = new PreferencesWindow(
            settingsStore ?? new SettingsStore(SettingsStore.DefaultDirectory()),
            preferencesTabIndex,
            new StartupFolderLaunchAtLoginService());
        window.ShortcutRecordingChanged += (_, isRecording) =>
        {
            if (hotkeys is null)
            {
                return;
            }

            if (isRecording)
            {
                hotkeys.Register(new Keymap(new Dictionary<AppAction, KeyChord>()));
                return;
            }

            RegisterHotkeys();
        };
        window.Closed += (_, _) => RegisterHotkeys();
        AttachClientOnlyDumpHook(window, dumpPath, shutdownAfterDump);
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
        AttachClientOnlyDumpHook(window, dumpPath, shutdownAfterDump);
        window.Show();
        window.Activate();
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

    private static void AttachClientOnlyDumpHook(Window window, string? dumpPath, bool shutdownAfterDump)
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

    private sealed record CaptureTarget(AppAction Action, PixelRect? Region, IntPtr? WindowHandle = null);

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
        int PreferencesTabIndex)
    {
        public static StartupOptions Parse(IReadOnlyList<string> args) =>
            new(
                args.Contains("--test-mode", StringComparer.OrdinalIgnoreCase),
                TryGetOptionValue(args, "--dump-window-png"),
                TryGetOptionValue(args, "--test-settings-dir"),
                TryGetThemeMode(TryGetOptionValue(args, "--test-app-theme")),
                TryGetPreferencesTabIndex(TryGetOptionValue(args, "--test-preferences-tab")));

        private static ThemeMode? TryGetThemeMode(string? value) =>
            value?.ToLowerInvariant() switch
            {
                "light" => ThemeMode.Light,
                "dark" => ThemeMode.Dark,
                "system" => ThemeMode.System,
                _ => null,
            };

        private static int TryGetPreferencesTabIndex(string? value) =>
            int.TryParse(value, out var index) ? Math.Clamp(index, 0, 5) : 0;
    }
}
