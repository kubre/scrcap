using System.IO;
using System.Windows;
using Scrcap.Core;
using Scrcap.Windows.Platform.Capture;
using Scrcap.Windows.Platform.Hotkeys;
using Scrcap.Windows.Platform.Tray;
using Scrcap.Windows.UI.Editor;
using Scrcap.Windows.UI.Overlay;
using Scrcap.Windows.UI.Preferences;

namespace Scrcap.Windows.UI;

public partial class App : System.Windows.Application
{
    private IGlobalHotkeyService? hotkeys;
    private ITrayService? tray;
    private SettingsStore? settingsStore;
    private IWindowsCaptureService? captureService;
    private IWindowSelectionService? windowSelectionService;
    private CaptureTarget? lastCapture;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        settingsStore = new SettingsStore(SettingsStore.DefaultDirectory());
        captureService = new WindowsCaptureService();
        windowSelectionService = new WindowSelectionService();
        hotkeys = new GlobalHotkeyService();
        RegisterHotkeys();
        hotkeys.Pressed += (_, action) => HandleCaptureAction(action);

        tray = new NotifyIconTrayService(new TaskbarThemeService());
        tray.CaptureRequested += (_, action) => HandleCaptureAction(action);
        tray.PreferencesRequested += (_, _) => OpenPreferences();
        tray.QuitRequested += (_, _) => Shutdown();
        tray.Show();

        var dumpPath = TryGetOptionValue(e.Args, "--dump-window-png");
        _ = TryGetOptionValue(e.Args, "--test-app-theme");
        _ = TryGetOptionValue(e.Args, "--test-taskbar-theme");

        if (e.Args.Contains("--open-preferences", StringComparer.OrdinalIgnoreCase))
        {
            OpenPreferences(dumpPath);
            return;
        }

        if (TryGetOptionValue(e.Args, "--open-sample-editor") is { } samplePath)
        {
            OpenSampleEditor(samplePath, dumpPath);
            return;
        }

        if (e.Args.Contains("--test-mode", StringComparer.OrdinalIgnoreCase))
        {
            OpenSampleEditor(null, dumpPath);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
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

        try
        {
            var capture = await CaptureAsync(action, settingsStore.Settings, CancellationToken.None).ConfigureAwait(true);
            if (capture is null)
            {
                return;
            }

            var behavior = BehaviorFor(action, settingsStore.Settings);
            if (behavior is AfterCaptureBehavior.CopyOnly or AfterCaptureBehavior.Both)
            {
                System.Windows.Clipboard.SetImage(EditorWindow.DecodePng(capture.PngBytes));
            }

            if (behavior is AfterCaptureBehavior.OpenEditor or AfterCaptureBehavior.Both)
            {
                OpenEditor(capture);
            }
        }
        catch (Exception ex)
        {
            tray?.Notify("scrcap capture failed", ex.Message);
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
            AppAction.CaptureWindow => await WindowTargetAsync(cancellationToken).ConfigureAwait(true),
            AppAction.CaptureFullscreen => new CaptureTarget(AppAction.CaptureFullscreen, null),
            AppAction.CaptureScrolling => await RegionTargetAsync(AppAction.CaptureScrolling, 0, cancellationToken).ConfigureAwait(true),
            _ => null,
        };

        if (target is null)
        {
            return null;
        }

        lastCapture = target;
        return await CaptureTargetAsync(target, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<CaptureTarget?> RegionTargetAsync(AppAction action, int countdownSeconds, CancellationToken cancellationToken)
    {
        var overlay = new OverlayWindow();
        var rect = await overlay.SelectRegionAsync(countdownSeconds).WaitAsync(cancellationToken).ConfigureAwait(true);
        return rect is null ? null : new CaptureTarget(action, rect.Value);
    }

    private async Task<CaptureTarget?> WindowTargetAsync(CancellationToken cancellationToken)
    {
        var overlay = new OverlayWindow();
        var point = await overlay.SelectPointAsync().WaitAsync(cancellationToken).ConfigureAwait(true);
        if (point is null || windowSelectionService?.WindowFromPoint(point.Value) is not { } candidate)
        {
            return null;
        }

        return new CaptureTarget(AppAction.CaptureWindow, candidate.Bounds, candidate.Hwnd);
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
            var hud = ScrollingCaptureHud.ShowFor(scrollingRegion, scrollingCancellation.Cancel);
            var progress = new Progress<ScrollingCaptureProgress>(hud.UpdateProgress);
            try
            {
                return await captureService.CaptureScrollingRegionAsync(
                    scrollingRegion,
                    request,
                    new ScrollingCaptureOptions(
                        settingsStore?.Settings.ScrollingMaxHeight ?? Settings.MaxScrollingMaxHeight,
                        Progress: progress),
                    scrollingCancellation.Token).ConfigureAwait(true);
            }
            finally
            {
                hud.Close();
            }
        }

        return await (target.Action switch
        {
            AppAction.CaptureRegion or AppAction.CaptureDelayed
                when target.Region is { } region => captureService.CaptureRegionAsync(region, request, cancellationToken),
            AppAction.CaptureWindow when target.WindowHandle is { } hwnd => captureService.CaptureWindowAsync(hwnd, request, cancellationToken),
            AppAction.CaptureFullscreen => captureService.CaptureMonitorUnderCursorAsync(request, cancellationToken),
            _ => throw new InvalidOperationException("No repeatable capture target is available."),
        }).ConfigureAwait(true);
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
        var window = new EditorWindow(capture, settingsStore?.Settings)
        {
            Title = capture.Metadata.WindowTitle is { Length: > 0 } title ? $"scrcap - {title}" : "scrcap",
        };
        window.Show();
        window.Activate();
    }

    private void OpenPreferences(string? dumpPath = null)
    {
        var window = new PreferencesWindow(settingsStore ?? new SettingsStore(SettingsStore.DefaultDirectory()));
        window.Closed += (_, _) => RegisterHotkeys();
        AttachDumpHook(window, dumpPath);
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

    private static void OpenSampleEditor(string? samplePath, string? dumpPath = null)
    {
        var window = samplePath is { Length: > 0 }
            ? OpenSampleCapture(samplePath)
            : new EditorWindow(settings: Settings.Defaults())
            {
                Title = "scrcap",
            };
        AttachDumpHook(window, dumpPath);
        window.Show();
        window.Activate();
    }

    private static EditorWindow OpenSampleCapture(string samplePath)
    {
        var bytes = File.ReadAllBytes(samplePath);
        var image = EditorWindow.DecodePng(bytes);
        return new EditorWindow(new CaptureResult(
            bytes,
            image.PixelWidth,
            image.PixelHeight,
            new CaptureMetadata(CaptureMode.Region, Path.GetFileName(samplePath), null, DateTimeOffset.Now)),
            Settings.Defaults())
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

    private static void AttachDumpHook(Window window, string? dumpPath)
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
            });
        };
    }

    private sealed record CaptureTarget(AppAction Action, PixelRect? Region, IntPtr? WindowHandle = null);
}
