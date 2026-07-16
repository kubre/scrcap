// AppDelegate — menu-bar resident (LSUIElement). Hotkeys are registered
// before any UI work; the overlay windows are
// pre-created so hotkey → overlay stays under 50 ms.

import AppKit
import ScrcapCore
#if DEBUG
import SwiftUI
#endif

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var settingsStore: SettingsStore!
    private var hotkeyService: HotkeyService!
    private var captureEngine: CaptureEngine!
    private var overlay: OverlayController!
    private var preferences: PreferencesWindowController!
    private var statusItem: NSStatusItem!
    private var scrollCapture: ScrollCaptureController?
    private var onboarding: OnboardingWindowController?
    private let countdown = CountdownController()
    private let firstLaunchNoticeShownKey = "hasShownFirstLaunchMenuBarNotice"

    /// For repeat-last (⌥⇧R): the previous region, or the previous mode.
    private enum LastCapture {
        case region(NSRect, NSScreen)
        case window(PickableWindow)
        case fullscreen(NSScreen)
    }
    private var lastCapture: LastCapture?
    private var permissionPollTimer: Timer?
    private var screenPermissionRequestInFlight = false
    private var updateCheckInFlight = false
    private var captureCancelMonitor: Any?

    private enum CapturePhase: Equatable {
        case selection
        case countdown
        case capture
        case scrolling
    }

    private var activeCapture: (token: UUID, phase: CapturePhase)?

    func applicationDidFinishLaunching(_ notification: Notification) {
        settingsStore = SettingsStore(directory: SettingsStore.defaultDirectory())

        applyTheme()

        // Hotkeys live before any UI work.
        hotkeyService = HotkeyService()
        hotkeyService.onAction = { [weak self] action in self?.perform(action) }
        let initialHotkeyResult = hotkeyService.register(keymap: settingsStore.settings.keymap)

        captureEngine = CaptureEngine()
        overlay = OverlayController()
        preferences = PreferencesWindowController(store: settingsStore)
        preferences.applyHotkeyRegistrationResult(initialHotkeyResult)

        setupStatusItem()

        NotificationCenter.default.addObserver(
            forName: .scrcapSettingsChanged, object: nil, queue: .main
        ) { [weak self] note in
            guard let self else { return }
            self.applyTheme()
            let result = self.hotkeyService.register(keymap: self.settingsStore.settings.keymap)
            (note.object as? SettingsModel)?.applyHotkeyRegistrationResult(result)
            self.rebuildMenu()
        }

        // While the shortcut recorder is armed, suspend global hotkeys so a
        // currently-bound chord can be re-recorded instead of firing a capture.
        NotificationCenter.default.addObserver(
            forName: .scrcapShortcutRecording, object: nil, queue: .main
        ) { [weak self] note in
            guard let self else { return }
            let recording = note.object as? Bool ?? false
            let result = self.hotkeyService.setSuspended(recording)
            if !recording {
                self.preferences.applyHotkeyRegistrationResult(result)
                self.rebuildMenu()
            }
        }

        NotificationCenter.default.addObserver(
            forName: .scrcapCheckForUpdates, object: nil, queue: .main
        ) { [weak self] _ in
            self?.checkForUpdates()
        }

        #if DEBUG
            if ProcessInfo.processInfo.environment["SCRCAP_SMOKE"] == "text" {
                runTextSmokeTest()
                return
            }
            if ProcessInfo.processInfo.environment["SCRCAP_SMOKE"] == "ui" {
                runUISnapshotSmokeTest()
                return
            }
        #endif

        // Screen Recording is requested lazily on the first capture. Asking on
        // launch stacks macOS' permission prompt with scrcap's own startup UI.
        presentFirstLaunchNoticeIfNeeded()
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    #if DEBUG
    /// Headless editor smoke test (no capture permission needed): opens the
    /// editor on a synthetic bitmap, drives the text tool, prints PASS/FAIL,
    /// and exits. Run with: SCRCAP_SMOKE=text .build/debug/scrcap
    private func runTextSmokeTest() {
        let width = 300, height = 200
        let ctx = CGContext(
            data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )!
        ctx.setFillColor(CGColor(gray: 0.5, alpha: 1))
        ctx.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let image = ctx.makeImage()!

        let editor = EditorWindowController(
            capture: CaptureResult(image: image, scale: 1),
            settingsStore: settingsStore
        )
        editor.show()
        // Let the window settle one runloop turn before driving it.
        DispatchQueue.main.async {
            let result = editor.debugExerciseTextTool()
            if result == "hello\nworld" {
                print("SMOKE PASS: text tool committed \(result.map { String(reflecting: $0) } ?? "nil")")
                exit(0)
            } else {
                print("SMOKE FAIL: got \(result.map { String(reflecting: $0) } ?? "nil")")
                exit(1)
            }
        }
    }

    /// Renders every chrome surface to /tmp/scrcap-ui-*.png and exits — a
    /// design review without Screen Recording permission. Run with:
    /// SCRCAP_SMOKE=ui .build/debug/scrcap
    private func runUISnapshotSmokeTest() {
        func writePNG(_ view: NSView, _ name: String) -> Bool {
            view.layoutSubtreeIfNeeded()
            guard let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { return false }
            view.cacheDisplay(in: view.bounds, to: rep)
            guard let data = rep.representation(using: .png, properties: [:]) else { return false }
            do {
                try data.write(to: URL(fileURLWithPath: "/tmp/scrcap-ui-\(name).png"))
            } catch {
                print("SMOKE FAIL: could not write \(name): \(error)")
                return false
            }
            print("wrote /tmp/scrcap-ui-\(name).png")
            return true
        }

        // Synthetic screenshot: soft gradient so annotations stay legible.
        let width = 720, height = 420
        let ctx = CGContext(
            data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )!
        let colors = [CGColor(red: 0.22, green: 0.26, blue: 0.36, alpha: 1),
                      CGColor(red: 0.46, green: 0.30, blue: 0.36, alpha: 1)]
        let gradient = CGGradient(colorsSpace: ctx.colorSpace, colors: colors as CFArray, locations: nil)!
        ctx.drawLinearGradient(gradient, start: .zero, end: CGPoint(x: width, y: height), options: [])
        let image = ctx.makeImage()!

        let editor = EditorWindowController(
            capture: CaptureResult(image: image, scale: 1),
            settingsStore: settingsStore
        )
        editor.show()

        DispatchQueue.main.async {
            var snapshots: Set<String> = []
            func snapshot(_ view: NSView, _ name: String) {
                if writePNG(view, name) { snapshots.insert(name) }
            }
            for (appearance, suffix) in [(NSAppearance.Name.aqua, "light"), (.darkAqua, "dark")] {
                NSApp.appearance = NSAppearance(named: appearance)
                if let content = editor.debugContentView {
                    snapshot(content, "editor-\(suffix)")
                }
            }
            NSApp.appearance = nil

            snapshot(ScrollCaptureController.debugHUDView(), "scroll-hud")

            if let screen = NSScreen.main {
                let overlay = OverlayView(frame: NSRect(x: 0, y: 0, width: 720, height: 420), screen: screen)
                overlay.reset(mode: .region)
                overlay.debugSetSelection(from: NSPoint(x: 180, y: 110), to: NSPoint(x: 540, y: 330))
                snapshot(overlay, "overlay-region")
                overlay.reset(mode: .window)
                snapshot(overlay, "overlay-window")
                overlay.stopAnimation()
            }

            let prefsHost = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 720, height: 500),
                styleMask: .borderless, backing: .buffered, defer: false
            )
            let prefsStore = SettingsStore(
                directory: FileManager.default.temporaryDirectory
                    .appendingPathComponent("scrcap-ui-smoke-\(UUID().uuidString)", isDirectory: true)
            )
            let prefsController = PreferencesViewController(model: SettingsModel(store: prefsStore))
            prefsHost.contentViewController = prefsController
            prefsHost.layoutIfNeeded()
            guard prefsController.debugExerciseTabs() else {
                print("SMOKE FAIL: preferences tab clicks")
                exit(1)
            }
            guard prefsController.debugExerciseDropdowns() else {
                print("SMOKE FAIL: preferences dropdowns")
                exit(1)
            }
            if let view = prefsHost.contentView {
                snapshot(view, "prefs")
            }
            let expected: Set<String> = [
                "editor-light", "editor-dark", "scroll-hud",
                "overlay-region", "overlay-window", "prefs",
            ]
            guard snapshots == expected else {
                print("SMOKE FAIL: missing snapshots \(expected.subtracting(snapshots).sorted())")
                exit(1)
            }
            exit(0)
        }
    }

    #endif

    // MARK: Theme

    private func applyTheme() {
        switch settingsStore.settings.themeMode {
        case .system:
            NSApp.appearance = nil
        case .light:
            NSApp.appearance = NSAppearance(named: .aqua)
        case .dark:
            NSApp.appearance = NSAppearance(named: .darkAqua)
        }
    }

    // MARK: Status item

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = statusItemImage() ?? NSImage(
            systemSymbolName: "camera.viewfinder",
            accessibilityDescription: "scrcap"
        )
        rebuildMenu()
    }

    private func statusItemImage() -> NSImage? {
        Theme.logoImage(size: 18, template: true)
    }

    private func presentFirstLaunchNoticeIfNeeded() {
        let defaults = UserDefaults.standard
        guard !defaults.bool(forKey: firstLaunchNoticeShownKey) else { return }

        defaults.set(true, forKey: firstLaunchNoticeShownKey)
        DispatchQueue.main.async { [weak self] in
            self?.presentFirstLaunchNotice()
        }
    }

    private func presentFirstLaunchNotice() {
        let regionShortcut = settingsStore.settings.keymap
            .chord(for: .captureRegion)?.displayValue ?? "⌥⇧1"
        let controller = OnboardingWindowController(
            regionShortcut: regionShortcut,
            onEnablePermission: { [weak self] in _ = self?.ensureScreenPermission() },
            onOpenPreferences: { [weak self] in self?.preferences.show() }
        )
        onboarding = controller
        controller.show()
    }

    private func rebuildMenu() {
        let menu = NSMenu()
        let keymap = settingsStore.settings.keymap

        func item(_ action: AppAction, selector: Selector) -> NSMenuItem {
            let item = NSMenuItem(title: action.title, action: selector, keyEquivalent: "")
            item.target = self
            if let chord = keymap.chord(for: action) {
                item.keyEquivalent = chord.key
                var flags: NSEvent.ModifierFlags = []
                if chord.modifiers.contains(.command) { flags.insert(.command) }
                if chord.modifiers.contains(.option) { flags.insert(.option) }
                if chord.modifiers.contains(.shift) { flags.insert(.shift) }
                if chord.modifiers.contains(.control) { flags.insert(.control) }
                item.keyEquivalentModifierMask = flags
            }
            return item
        }

        for action in AppAction.shortcutOrder {
            switch action {
            case .captureRegion:
                menu.addItem(item(action, selector: #selector(menuCaptureRegion)))
            case .captureWindow:
                menu.addItem(item(action, selector: #selector(menuCaptureWindow)))
            case .captureFullscreen:
                menu.addItem(item(action, selector: #selector(menuCaptureFullscreen)))
            case .captureScrolling:
                menu.addItem(item(action, selector: #selector(menuCaptureScrolling)))
            case .captureDelayed:
                menu.addItem(item(action, selector: #selector(menuCaptureDelayed)))
            case .repeatLast:
                menu.addItem(item(action, selector: #selector(menuRepeatLast)))
            }
        }
        menu.addItem(.separator())

        let updates = NSMenuItem(title: "Check for Updates…", action: #selector(menuCheckForUpdates), keyEquivalent: "")
        updates.target = self
        menu.addItem(updates)

        let prefs = NSMenuItem(title: "Preferences…", action: #selector(menuPreferences), keyEquivalent: ",")
        prefs.target = self
        menu.addItem(prefs)
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit scrcap", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)

        statusItem.menu = menu
    }

    @objc private func menuCaptureRegion() { perform(.captureRegion) }
    @objc private func menuCaptureWindow() { perform(.captureWindow) }
    @objc private func menuCaptureFullscreen() { perform(.captureFullscreen) }
    @objc private func menuCaptureScrolling() { perform(.captureScrolling) }
    @objc private func menuCaptureDelayed() { perform(.captureDelayed) }
    @objc private func menuRepeatLast() { perform(.repeatLast) }
    @objc private func menuPreferences() { preferences.show() }
    @objc private func menuCheckForUpdates() { checkForUpdates() }

    private var currentAppVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }

    private func checkForUpdates() {
        guard !updateCheckInFlight else { return }
        updateCheckInFlight = true
        let currentVersion = currentAppVersion
        Task { @MainActor in
            defer { updateCheckInFlight = false }
            do {
                let result = try await GitHubUpdateChecker().check(currentVersion: currentVersion)
                self.presentUpdateResult(result)
            } catch {
                self.presentUpdateError(error)
            }
        }
    }

    private func presentUpdateResult(_ result: UpdateCheckResult) {
        let alert = NSAlert()
        alert.alertStyle = .informational

        switch result {
        case .updateAvailable(let currentVersion, let release):
            alert.messageText = "Update Available"
            alert.informativeText = "scrcap \(release.tagName) is available. You are running \(versionLabel(currentVersion))."
            alert.addButton(withTitle: "Open GitHub Release")
            alert.addButton(withTitle: "Later")
            showUpdateAlert(alert, releaseURL: release.htmlURL)
        case .upToDate(let currentVersion, let release):
            alert.messageText = "scrcap is up to date"
            alert.informativeText = "You are running \(versionLabel(currentVersion)). Latest release is \(release.tagName)."
            alert.addButton(withTitle: "OK")
            showUpdateAlert(alert)
        case .cannotCompare(let currentVersion, let release):
            alert.messageText = "Latest Release"
            alert.informativeText = "Latest release is \(release.tagName). This build reports \(versionLabel(currentVersion)), so scrcap could not compare versions."
            alert.addButton(withTitle: "Open GitHub Release")
            alert.addButton(withTitle: "OK")
            showUpdateAlert(alert, releaseURL: release.htmlURL)
        }
    }

    private func presentUpdateError(_ error: Error) {
        let alert = NSAlert()
        alert.messageText = "Could not check for updates"
        alert.informativeText = error.localizedDescription
        alert.alertStyle = .warning
        alert.addButton(withTitle: "OK")
        showUpdateAlert(alert)
    }

    private func showUpdateAlert(_ alert: NSAlert, releaseURL: URL? = nil) {
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn, let releaseURL {
            NSWorkspace.shared.open(releaseURL)
        }
    }

    private func versionLabel(_ version: String) -> String {
        version == "dev" ? "a dev build" : "v\(version)"
    }

    // MARK: Actions

    private func perform(_ action: AppAction) {
        guard ensureScreenPermission() else { return }
        recoverCancelledSelection()
        guard activeCapture == nil else { return }
        captureEngine.includeCursor = settingsStore.settings.includeCursor
        captureEngine.windowBackground = settingsStore.settings.windowBackgroundTransparent
            ? nil
            : (NSColor(hex: settingsStore.settings.windowBackgroundHex) ?? .white)

        switch action {
        case .captureFullscreen:
            let screen = GeometryMapper.screenUnderMouse()
            let token = beginSession(.capture)
            runCapture(token: token, mode: .fullscreen, repeatValue: .fullscreen(screen), metadata: { .fullscreen(on: screen) }) {
                try await self.captureEngine.captureDisplay(screen: screen)
            }

        case .captureRegion:
            let token = beginSession(.selection)
            overlay.beginRegionSelection { [weak self] rect, screen in
                guard let self, self.moveSession(token, to: .capture) else { return }
                self.runCapture(token: token, mode: .region, repeatValue: .region(rect, screen), metadata: { .region(rect, on: screen) }) {
                    try await self.captureEngine.captureRegion(rect, on: screen)
                }
            }

        case .captureWindow:
            switch settingsStore.settings.windowCaptureTarget {
            case .active:
                if let window = OverlayController.listWindows().first {
                    let token = beginSession(.capture)
                    captureWindow(window, token: token)
                } else {
                    beginWindowPick(token: beginSession(.selection))
                }
            case .selected:
                beginWindowPick(token: beginSession(.selection))
            }

        case .captureScrolling:
            startScrollingCapture(token: beginSession(.selection))

        case .captureDelayed:
            startDelayedCapture(token: beginSession(.selection))

        case .repeatLast:
            repeatLastCapture()
        }
    }

    private func startDelayedCapture(token: UUID) {
        overlay.beginRegionSelection { [weak self] rect, screen in
            guard let self, self.moveSession(token, to: .countdown) else { return }
            let seconds = self.settingsStore.settings.captureDelaySeconds
            self.countdown.start(
                seconds: seconds,
                on: screen,
                onCancel: { [weak self] in self?.finishSession(token) }
            ) { [weak self] in
                guard let self, self.moveSession(token, to: .capture) else { return }
                self.runCapture(token: token, mode: .region, repeatValue: .region(rect, screen), metadata: { .region(rect, on: screen) }) {
                    try await self.captureEngine.captureRegion(rect, on: screen)
                }
            }
        }
    }

    private func beginWindowPick(token: UUID) {
        overlay.beginWindowPick { [weak self] window in
            guard let self, self.moveSession(token, to: .capture) else { return }
            self.captureWindow(window, token: token)
        }
    }

    private func captureWindow(_ window: PickableWindow, token: UUID) {
        let includeShadow = settingsStore.settings.includeWindowShadow
        runCapture(token: token, mode: .window, repeatValue: .window(window), metadata: { .window(window) }) {
            try await self.captureEngine.captureWindow(
                windowID: window.windowID,
                includeShadow: includeShadow
            )
        }
    }

    private func repeatLastCapture() {
        switch lastCapture {
        case .region(let rect, let screen):
            let token = beginSession(.capture)
            runCapture(token: token, mode: .region, metadata: { .region(rect, on: screen) }) {
                try await self.captureEngine.captureRegion(rect, on: screen)
            }
        case .window(let window):
            let token = beginSession(.capture)
            let includeShadow = settingsStore.settings.includeWindowShadow
            runCapture(token: token, mode: .window, metadata: { .window(window) }) {
                try await self.captureEngine.captureWindow(windowID: window.windowID, includeShadow: includeShadow)
            }
        case .fullscreen(let screen):
            let token = beginSession(.capture)
            runCapture(token: token, mode: .fullscreen, metadata: { .fullscreen(on: screen) }) {
                try await self.captureEngine.captureDisplay(screen: screen)
            }
        case nil:
            break // nothing captured yet this session
        }
    }

    private func startScrollingCapture(token: UUID) {
        guard ScrollCaptureController.ensureAccessibility() else {
            finishSession(token)
            presentError(ScrollingCaptureError.accessibilityPermissionRequired)
            return
        }
        overlay.beginRegionSelection { [weak self] rect, screen in
            guard let self, self.moveSession(token, to: .scrolling) else { return }
            let metadata = CaptureMetadata.region(rect, on: screen, mode: .scrolling)
            self.captureEngine.includeCursor = false
            let controller = ScrollCaptureController(capture: self.captureEngine, settingsStore: self.settingsStore)
            self.scrollCapture = controller
            controller.run(rect: rect, screen: screen) { [weak self] result in
                guard let self, self.activeCapture?.token == token else { return }
                self.scrollCapture = nil
                self.finishSession(token)
                switch result {
                case .success(let capture):
                    self.deliver(capture.withMetadata(metadata), mode: .scrolling)
                case .failure(let error):
                    self.presentError(error)
                }
            }
        }
    }

    private func runCapture(
        token: UUID,
        mode: CaptureMode,
        repeatValue: LastCapture? = nil,
        metadata: @escaping () -> CaptureMetadata? = { nil },
        _ body: @escaping () async throws -> CaptureResult
    ) {
        Task { @MainActor in
            do {
                let result = try await body()
                guard self.activeCapture?.token == token else { return }
                if let repeatValue { self.lastCapture = repeatValue }
                self.deliver(result.withMetadata(metadata()), mode: mode)
                self.finishSession(token)
            } catch {
                guard self.activeCapture?.token == token else { return }
                self.presentError(error)
                self.finishSession(token)
            }
        }
    }

    private func beginSession(_ phase: CapturePhase) -> UUID {
        let token = UUID()
        activeCapture = (token, phase)
        if phase == .selection {
            monitorCancellation(token: token)
        }
        return token
    }

    @discardableResult
    private func moveSession(_ token: UUID, to phase: CapturePhase) -> Bool {
        guard activeCapture?.token == token else { return false }
        activeCapture = (token, phase)
        removeCancelMonitor()
        return true
    }

    private func finishSession(_ token: UUID) {
        guard activeCapture?.token == token else { return }
        activeCapture = nil
        removeCancelMonitor()
    }

    private func recoverCancelledSelection() {
        guard let activeCapture, activeCapture.phase == .selection, !overlay.isActive else { return }
        finishSession(activeCapture.token)
    }

    private func monitorCancellation(token: UUID) {
        removeCancelMonitor()
        captureCancelMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 53 else { return event }
            DispatchQueue.main.async { self?.finishSession(token) }
            return event
        }
    }

    private func removeCancelMonitor() {
        if let captureCancelMonitor { NSEvent.removeMonitor(captureCancelMonitor) }
        captureCancelMonitor = nil
    }

    private func deliver(_ capture: CaptureResult, mode: CaptureMode) {
        let behavior = settingsStore.settings.behavior(for: mode)
        if behavior == .copyOnly || behavior == .both {
            if Exporter.copyToClipboard(capture.image, pointScale: capture.scale, metadata: capture.metadata) {
                if !settingsStore.settings.suppressCopyNotification {
                    Notifier.copiedToClipboard()
                }
            } else {
                presentError(ExportError.clipboardWriteFailed)
            }
        }
        if behavior == .openEditor || behavior == .both {
            EditorWindowController(capture: capture, settingsStore: settingsStore).show()
        }
    }

    private func presentError(_ error: Error) {
        NSLog("scrcap: capture failed: \(error.localizedDescription)")
        // A menu-bar app failing silently looks like it did nothing at all.
        let alert = NSAlert()
        alert.messageText = "Capture failed"
        alert.informativeText = error.localizedDescription
        alert.alertStyle = .warning
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }

    // MARK: Screen Recording permission (TCC)

    @discardableResult
    private func ensureScreenPermission() -> Bool {
        if CGPreflightScreenCaptureAccess() {
            screenPermissionRequestInFlight = false
            return true
        }
        requestScreenPermission()
        return false
    }

    private func requestScreenPermission() {
        guard !screenPermissionRequestInFlight else { return }
        screenPermissionRequestInFlight = true

        CGRequestScreenCaptureAccess()

        // Live-detect when permission lands.
        permissionPollTimer?.invalidate()
        let startedAt = Date()
        permissionPollTimer = Timer.scheduledTimer(withTimeInterval: 1.5, repeats: true) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }
            if CGPreflightScreenCaptureAccess() {
                timer.invalidate()
                self.permissionPollTimer = nil
                self.screenPermissionRequestInFlight = false
            } else if Date().timeIntervalSince(startedAt) > 300 {
                timer.invalidate()
                self.permissionPollTimer = nil
                self.screenPermissionRequestInFlight = false
            }
        }
    }
}
