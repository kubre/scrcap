// AppDelegate — menu-bar resident (LSUIElement). Hotkeys are registered
// before any UI work; the overlay windows are
// pre-created so hotkey → overlay stays under 50 ms.

import AppKit
import ScrcapCore

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var settingsStore: SettingsStore!
    private var hotkeyService: HotkeyService!
    private var captureEngine: CaptureEngine!
    private var overlay: OverlayController!
    private var preferences: PreferencesWindowController!
    private var statusItem: NSStatusItem!
    private var scrollCapture: ScrollCaptureController?

    /// For repeat-last (⌥⇧R): the previous region, or the previous mode.
    private enum LastCapture {
        case region(NSRect, NSScreen)
        case window(CGWindowID)
        case fullscreen(NSScreen)
    }
    private var lastCapture: LastCapture?
    private var permissionPollTimer: Timer?
    private var screenPermissionRequestInFlight = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        settingsStore = SettingsStore(directory: SettingsStore.defaultDirectory())

        // Hotkeys live before any UI work.
        hotkeyService = HotkeyService()
        hotkeyService.onAction = { [weak self] action in self?.perform(action) }
        hotkeyService.register(keymap: settingsStore.settings.keymap)

        captureEngine = CaptureEngine()
        overlay = OverlayController()
        preferences = PreferencesWindowController(store: settingsStore)

        setupStatusItem()

        NotificationCenter.default.addObserver(
            forName: .scrcapSettingsChanged, object: nil, queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            self.hotkeyService.register(keymap: self.settingsStore.settings.keymap)
            self.rebuildMenu()
        }

        // While the shortcut recorder is armed, suspend global hotkeys so a
        // currently-bound chord can be re-recorded instead of firing a capture.
        NotificationCenter.default.addObserver(
            forName: .scrcapShortcutRecording, object: nil, queue: .main
        ) { [weak self] note in
            guard let self else { return }
            let recording = note.object as? Bool ?? false
            self.hotkeyService.register(
                keymap: recording ? Keymap(bindings: [:]) : self.settingsStore.settings.keymap
            )
        }

        #if DEBUG
            if ProcessInfo.processInfo.environment["SCRCAP_SMOKE"] == "text" {
                runTextSmokeTest()
                return
            }
        #endif

        // Screen Recording is requested lazily on the first capture. Asking on
        // launch stacks macOS' permission prompt with scrcap's own startup UI.
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
    #endif

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
        let resourceURL = Bundle.main.url(forResource: "MenuBarIconTemplate", withExtension: "png")
            ?? Bundle.module.url(forResource: "MenuBarIconTemplate", withExtension: "png")
        guard let resourceURL, let image = NSImage(contentsOf: resourceURL) else { return nil }
        image.size = NSSize(width: 18, height: 18)
        image.isTemplate = true
        image.accessibilityDescription = "scrcap"
        return image
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
            case .repeatLast:
                menu.addItem(item(action, selector: #selector(menuRepeatLast)))
            }
        }
        menu.addItem(.separator())

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
    @objc private func menuRepeatLast() { perform(.repeatLast) }
    @objc private func menuPreferences() { preferences.show() }

    // MARK: Actions

    private func perform(_ action: AppAction) {
        guard ensureScreenPermission() else { return }
        guard !overlay.isActive else { return }

        switch action {
        case .captureFullscreen:
            let screen = GeometryMapper.screenUnderMouse()
            lastCapture = .fullscreen(screen)
            runCapture(mode: .fullscreen) { try await self.captureEngine.captureDisplay(screen: screen) }

        case .captureRegion:
            overlay.beginRegionSelection { [weak self] rect, screen in
                guard let self else { return }
                self.lastCapture = .region(rect, screen)
                self.runCapture(mode: .region) { try await self.captureEngine.captureRegion(rect, on: screen) }
            }

        case .captureWindow:
            overlay.beginWindowPick { [weak self] window in
                guard let self else { return }
                self.lastCapture = .window(window.windowID)
                let includeShadow = self.settingsStore.settings.includeWindowShadow
                self.runCapture(mode: .window) {
                    try await self.captureEngine.captureWindow(
                        windowID: window.windowID,
                        includeShadow: includeShadow
                    )
                }
            }

        case .captureScrolling:
            startScrollingCapture()

        case .repeatLast:
            repeatLastCapture()
        }
    }

    private func repeatLastCapture() {
        switch lastCapture {
        case .region(let rect, let screen):
            runCapture(mode: .region) { try await self.captureEngine.captureRegion(rect, on: screen) }
        case .window(let id):
            let includeShadow = settingsStore.settings.includeWindowShadow
            runCapture(mode: .window) {
                try await self.captureEngine.captureWindow(windowID: id, includeShadow: includeShadow)
            }
        case .fullscreen(let screen):
            runCapture(mode: .fullscreen) { try await self.captureEngine.captureDisplay(screen: screen) }
        case nil:
            break // nothing captured yet this session
        }
    }

    private func startScrollingCapture() {
        guard ScrollCaptureController.ensureAccessibility() else { return }
        overlay.beginRegionSelection { [weak self] rect, screen in
            guard let self else { return }
            let controller = ScrollCaptureController(capture: self.captureEngine, settingsStore: self.settingsStore)
            self.scrollCapture = controller
            controller.run(rect: rect, screen: screen) { [weak self] result in
                guard let self else { return }
                self.scrollCapture = nil
                switch result {
                case .success(let capture):
                    self.deliver(capture, mode: .scrolling)
                case .failure(let error):
                    self.presentError(error)
                }
            }
        }
    }

    private func runCapture(mode: CaptureMode, _ body: @escaping () async throws -> CaptureResult) {
        Task { @MainActor in
            do {
                let result = try await body()
                self.deliver(result, mode: mode)
            } catch {
                self.presentError(error)
            }
        }
    }

    private func deliver(_ capture: CaptureResult, mode: CaptureMode) {
        let behavior = settingsStore.settings.behavior(for: mode)
        if behavior == .copyOnly || behavior == .both {
            Exporter.copyToClipboard(capture.image)
        }
        if behavior == .openEditor || behavior == .both {
            EditorWindowController(capture: capture, settingsStore: settingsStore).show()
        }
    }

    private func presentError(_ error: Error) {
        NSLog("scrcap: capture failed: \(error.localizedDescription)")
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
