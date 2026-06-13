// Preferences — SwiftUI is allowed here: latency is irrelevant and the
// frameworks ship with macOS. Same design language as the editor: compact,
// red-accented, system text for labels, monospaced only for shortcuts/readouts.

import AppKit
import SwiftUI
import ServiceManagement
import ScrcapCore

// SwiftUI declares its own `Settings` scene type; disambiguate.
typealias AppSettings = ScrcapCore.Settings

extension Notification.Name {
    static let scrcapSettingsChanged = Notification.Name("scrcapSettingsChanged")
    static let scrcapCheckForUpdates = Notification.Name("scrcapCheckForUpdates")
    /// object: Bool — true while the shortcut recorder is armed. Global
    /// hotkeys pause so pressing a currently-bound chord re-records it
    /// instead of firing a capture.
    static let scrcapShortcutRecording = Notification.Name("scrcapShortcutRecording")
}

private enum PrefColor {
    static let accent = Color(nsColor: Theme.accent)
    static let accentDeep = Color(nsColor: Theme.accentDeep)
    static let onAccent = Color(nsColor: Theme.onAccent)
    static let paper = Color(nsColor: Theme.well)
    static let chrome = Color(nsColor: Theme.chrome)
    static let ink = Color(nsColor: Theme.ink)
    static let inkDim = Color(nsColor: Theme.inkDim)
    static let hairline = Color(nsColor: Theme.hairline)
    static let field = Color.primary.opacity(0.045)
    static let warn = Color(red: 1.0, green: 0.23, blue: 0.19)
}

private enum PrefFont {
    static let body = Font.system(size: 12)
    static let label = Font.system(size: 12, weight: .medium)
    static let section = Font.system(size: 10, weight: .bold)
    static let mono = Font.system(size: 11, weight: .medium, design: .monospaced)
    static let monoSmall = Font.system(size: 10, weight: .medium, design: .monospaced)
}

final class SettingsModel: ObservableObject {
    let store: SettingsStore
    /// A @Published mirror of the store's settings. Using real published state
    /// (rather than a manual objectWillChange on a computed pass-through) is
    /// what makes SwiftUI controls reliably reflect changes: @Published emits
    /// objectWillChange *before* the value changes, as SwiftUI requires.
    @Published private(set) var settings: AppSettings

    init(store: SettingsStore) {
        self.store = store
        self.settings = store.settings
    }

    func update(_ mutate: (inout AppSettings) -> Void) {
        let saved = store.update(mutate) // persist + normalize (source of truth)
        settings = store.settings        // re-sync the published mirror → notifies SwiftUI
        guard saved else {
            presentSaveFailure()
            return
        }
        NotificationCenter.default.post(name: .scrcapSettingsChanged, object: nil)
    }

    private func presentSaveFailure() {
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "Preferences were not saved"
        alert.informativeText = "scrcap could not write settings.json. Check the Application Support folder permissions and try again."
        alert.addButton(withTitle: "OK")
        alert.runModal()
    }
}

final class PreferencesWindowController {
    private var window: NSWindow?
    private let model: SettingsModel

    init(store: SettingsStore) {
        model = SettingsModel(store: store)
    }

    func show() {
        if window == nil {
            let w = NSWindow(contentViewController: PreferencesViewController(model: model))
            w.title = "scrcap"
            // Match the editor's frameless rounded chrome (see EditorWindow):
            // transparent titlebar, content view draws its own rounded border,
            // window background matches the chrome so the corner arc blends.
            w.styleMask = [.titled, .closable, .fullSizeContentView]
            w.titlebarAppearsTransparent = true
            w.titleVisibility = .hidden
            w.isOpaque = true
            w.backgroundColor = Theme.chrome
            w.hasShadow = true
            w.isMovableByWindowBackground = true
            w.isReleasedWhenClosed = false
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }
}

// MARK: - Root

private enum PrefTab: String, CaseIterable, Hashable, Identifiable {
    case general
    case capture
    case shortcuts
    case editor
    case output
    case about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .general: return "General"
        case .capture: return "Capture"
        case .shortcuts: return "Shortcuts"
        case .editor: return "Editor"
        case .output: return "Output"
        case .about: return "About"
        }
    }

    var symbolName: String {
        switch self {
        case .general: return "gearshape"
        case .capture: return "camera.viewfinder"
        case .shortcuts: return "command"
        case .editor: return "pencil.and.outline"
        case .output: return "square.and.arrow.up"
        case .about: return "info.circle"
        }
    }
}

final class PreferencesViewController: NSViewController {
    private let model: SettingsModel
    private let contentView = NSView()
    private var tabBar: PrefTabBar?
    private var currentPane: NSViewController?

    init(model: SettingsModel) {
        self.model = model
        super.init(nibName: nil, bundle: nil)
    }

    required init?(coder: NSCoder) {
        nil
    }

    override func loadView() {
        // Rounded chrome identical to the editor window's content view.
        let root = EditorContainerView(frame: NSRect(x: 0, y: 0, width: 720, height: 540))
        view = root

        let header = PrefHeaderView()
        header.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(header)

        let bar = PrefTabBar { [weak self] tab in self?.showPane(tab) }
        bar.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(bar)
        tabBar = bar

        contentView.wantsLayer = true
        contentView.layer?.backgroundColor = Theme.well.cgColor
        contentView.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(contentView)

        NSLayoutConstraint.activate([
            header.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            header.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            header.topAnchor.constraint(equalTo: root.topAnchor),
            header.heightAnchor.constraint(equalToConstant: Theme.headerHeight),

            bar.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            bar.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            bar.topAnchor.constraint(equalTo: header.bottomAnchor),
            bar.heightAnchor.constraint(equalToConstant: Theme.toolbarHeight),

            contentView.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            contentView.trailingAnchor.constraint(equalTo: root.trailingAnchor),
            contentView.topAnchor.constraint(equalTo: bar.bottomAnchor),
            contentView.bottomAnchor.constraint(equalTo: root.bottomAnchor),
        ])

        tabBar?.select(.general)
    }

    private func showPane(_ tab: PrefTab) {
        currentPane?.view.removeFromSuperview()
        currentPane?.removeFromParent()

        let pane = makePane(for: tab)
        addChild(pane)
        pane.view.translatesAutoresizingMaskIntoConstraints = false
        contentView.addSubview(pane.view)
        NSLayoutConstraint.activate([
            pane.view.leadingAnchor.constraint(equalTo: contentView.leadingAnchor),
            pane.view.trailingAnchor.constraint(equalTo: contentView.trailingAnchor),
            pane.view.topAnchor.constraint(equalTo: contentView.topAnchor),
            pane.view.bottomAnchor.constraint(equalTo: contentView.bottomAnchor),
        ])
        currentPane = pane
    }

    private func makePane(for tab: PrefTab) -> NSViewController {
        switch tab {
        case .general:
            return NSHostingController(rootView: PreferencesPane { GeneralTab(model: model) })
        case .capture:
            return NSHostingController(rootView: PreferencesPane { CaptureTab(model: model) })
        case .shortcuts:
            return NSHostingController(rootView: PreferencesPane { ShortcutsTab(model: model) })
        case .editor:
            return NSHostingController(rootView: PreferencesPane { EditorTab(model: model) })
        case .output:
            return NSHostingController(rootView: PreferencesPane { OutputTab(model: model) })
        case .about:
            return NSHostingController(rootView: PreferencesPane { AboutTab() })
        }
    }

    #if DEBUG
    func debugExerciseTabs() -> Bool {
        tabBar?.debugClickAllTabs() == true && currentPane != nil
    }

    func debugExerciseDropdowns() -> Bool {
        tabBar?.select(.capture)
        view.layoutSubtreeIfNeeded()
        guard let capturePopups = currentPane?.view.debugDescendants(of: NSPopUpButton.self),
              let regionPopup = capturePopups.first,
              let windowPopup = capturePopups.last
        else { return false }

        regionPopup.selectItem(at: 1)
        regionPopup.sendAction(regionPopup.action, to: regionPopup.target)
        windowPopup.selectItem(at: 1)
        windowPopup.sendAction(windowPopup.action, to: windowPopup.target)
        view.layoutSubtreeIfNeeded()
        guard model.settings.behavior(for: .region) == .copyOnly,
              regionPopup.indexOfSelectedItem == 1,
              regionPopup.titleOfSelectedItem == "Copy only",
              model.settings.windowCaptureTarget == .selected,
              windowPopup.indexOfSelectedItem == 1,
              windowPopup.titleOfSelectedItem == "Pick on screen"
        else { return false }

        tabBar?.select(.editor)
        view.layoutSubtreeIfNeeded()
        guard let editorPopups = currentPane?.view.debugDescendants(of: NSPopUpButton.self),
              editorPopups.count == 2
        else { return false }

        let returnPopup = editorPopups[0]
        let escPopup = editorPopups[1]
        returnPopup.selectItem(at: 1)
        returnPopup.sendAction(returnPopup.action, to: returnPopup.target)
        escPopup.selectItem(at: 1)
        escPopup.sendAction(escPopup.action, to: escPopup.target)
        view.layoutSubtreeIfNeeded()

        return model.settings.textEnterBehavior == .commit
            && returnPopup.indexOfSelectedItem == 1
            && returnPopup.titleOfSelectedItem == "Commit text"
            && model.settings.escBehavior == .closeOnly
            && escPopup.indexOfSelectedItem == 1
            && escPopup.titleOfSelectedItem == "Close only"
    }
    #endif
}

#if DEBUG
private extension NSView {
    func debugDescendants<T: NSView>(of type: T.Type) -> [T] {
        var matches: [T] = []
        for subview in subviews {
            if let typed = subview as? T {
                matches.append(typed)
            }
            matches.append(contentsOf: subview.debugDescendants(of: type))
        }
        return matches
    }
}
#endif

// MARK: - Chrome (brand header + editor-style tab strip)

/// Brand strip mirroring the editor's EditorHeaderView: logo + wordmark on the
/// left (offset to clear the floating traffic-light buttons), 1px rule below,
/// draggable.
private final class PrefHeaderView: NSView {
    private let brandIcon = NSImageView(image: Theme.logoImage(size: 16, template: true) ?? NSImage())
    private let brandLabel = NSTextField(labelWithString: Theme.brandName)
    private let rule = NSView()

    override var mouseDownCanMoveWindow: Bool { true }

    init() {
        super.init(frame: .zero)
        wantsLayer = true

        brandIcon.imageScaling = .scaleProportionallyDown
        brandIcon.translatesAutoresizingMaskIntoConstraints = false
        addSubview(brandIcon)

        brandLabel.font = Theme.brandFont
        brandLabel.translatesAutoresizingMaskIntoConstraints = false
        addSubview(brandLabel)

        rule.wantsLayer = true
        rule.translatesAutoresizingMaskIntoConstraints = false
        addSubview(rule)

        NSLayoutConstraint.activate([
            brandIcon.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 92),
            brandIcon.centerYAnchor.constraint(equalTo: centerYAnchor),
            brandIcon.widthAnchor.constraint(equalToConstant: 16),
            brandIcon.heightAnchor.constraint(equalToConstant: 16),
            brandLabel.leadingAnchor.constraint(equalTo: brandIcon.trailingAnchor, constant: 7),
            brandLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            rule.leadingAnchor.constraint(equalTo: leadingAnchor),
            rule.trailingAnchor.constraint(equalTo: trailingAnchor),
            rule.bottomAnchor.constraint(equalTo: bottomAnchor),
            rule.heightAnchor.constraint(equalToConstant: 1),
        ])
        applyColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.chrome.cgColor
            brandIcon.contentTintColor = Theme.ink
            brandLabel.textColor = Theme.ink
            rule.layer?.backgroundColor = Theme.rule.cgColor
        }
    }
}

/// Horizontal strip of PrefTabCells with hairline dividers and a 1px top rule —
/// built like EditorToolbar, but the cells persist a selection.
private final class PrefTabBar: NSView {
    private let onSelect: (PrefTab) -> Void
    private var cells: [PrefTab: PrefTabCell] = [:]
    private let rule = NSView()

    init(onSelect: @escaping (PrefTab) -> Void) {
        self.onSelect = onSelect
        super.init(frame: .zero)
        wantsLayer = true

        let stack = NSStackView()
        stack.orientation = .horizontal
        stack.spacing = 0
        stack.alignment = .centerY
        stack.translatesAutoresizingMaskIntoConstraints = false
        addSubview(stack)

        for (index, tab) in PrefTab.allCases.enumerated() {
            if index > 0 { stack.addArrangedSubview(makeDivider()) }
            let cell = PrefTabCell(tab: tab) { [weak self] in self?.select(tab) }
            cells[tab] = cell
            stack.addArrangedSubview(cell)
        }

        rule.wantsLayer = true
        rule.translatesAutoresizingMaskIntoConstraints = false
        addSubview(rule)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: leadingAnchor),
            stack.centerYAnchor.constraint(equalTo: centerYAnchor),
            rule.leadingAnchor.constraint(equalTo: leadingAnchor),
            rule.trailingAnchor.constraint(equalTo: trailingAnchor),
            rule.topAnchor.constraint(equalTo: topAnchor),
            rule.heightAnchor.constraint(equalToConstant: 1),
        ])
        applyColors()
    }

    required init?(coder: NSCoder) { fatalError() }

    func select(_ tab: PrefTab) {
        for (key, cell) in cells { cell.isActive = (key == tab) }
        onSelect(tab)
    }

    #if DEBUG
    func debugClickAllTabs() -> Bool {
        for tab in PrefTab.allCases {
            guard let cell = cells[tab] else { return false }
            cell.mouseDown(with: NSEvent())
            guard cell.isActive else { return false }
        }
        return true
    }
    #endif

    private func makeDivider() -> NSView {
        let divider = HairlineDivider()
        NSLayoutConstraint.activate([
            divider.widthAnchor.constraint(equalToConstant: 1),
            divider.heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
        ])
        return divider
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }

    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.chrome.cgColor
            rule.layer?.backgroundColor = Theme.rule.cgColor
        }
    }
}

/// 1px vertical divider that re-tints with the appearance.
private final class HairlineDivider: NSView {
    init() {
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false
        applyColors()
    }
    required init?(coder: NSCoder) { fatalError() }
    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        applyColors()
    }
    private func applyColors() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            layer?.backgroundColor = Theme.hairline.cgColor
        }
    }
}

/// Flat tab cell modeled on the editor's ToolbarCell: SF icon + label, with a
/// solid red block when selected and a hover wash otherwise.
private final class PrefTabCell: NSControl {
    var isActive = false {
        didSet { updateAppearance() }
    }
    private var isHovered = false {
        didSet { updateAppearance() }
    }
    private let iconView = NSImageView()
    private let titleLabel: NSTextField
    private let onClick: () -> Void

    override var mouseDownCanMoveWindow: Bool { false }

    init(tab: PrefTab, onClick: @escaping () -> Void) {
        self.onClick = onClick
        titleLabel = NSTextField(labelWithString: tab.title)
        super.init(frame: .zero)
        wantsLayer = true
        translatesAutoresizingMaskIntoConstraints = false

        iconView.image = NSImage(systemSymbolName: tab.symbolName, accessibilityDescription: tab.title)?
            .withSymbolConfiguration(Theme.iconConfiguration)
        iconView.imageScaling = .scaleProportionallyDown
        iconView.translatesAutoresizingMaskIntoConstraints = false

        titleLabel.font = Theme.cellFont
        titleLabel.translatesAutoresizingMaskIntoConstraints = false

        addSubview(iconView)
        addSubview(titleLabel)
        NSLayoutConstraint.activate([
            heightAnchor.constraint(equalToConstant: Theme.toolbarHeight - 1),
            iconView.widthAnchor.constraint(equalToConstant: 15),
            iconView.heightAnchor.constraint(equalToConstant: 15),
            iconView.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 12),
            iconView.centerYAnchor.constraint(equalTo: centerYAnchor),
            titleLabel.leadingAnchor.constraint(equalTo: iconView.trailingAnchor, constant: 6),
            titleLabel.centerYAnchor.constraint(equalTo: centerYAnchor),
            titleLabel.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -12),
        ])
        updateAppearance()
    }

    required init?(coder: NSCoder) { fatalError() }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .activeInActiveApp, .inVisibleRect],
            owner: self
        ))
    }

    override func mouseEntered(with event: NSEvent) { isHovered = true }
    override func mouseExited(with event: NSEvent) { isHovered = false }
    override func mouseDown(with event: NSEvent) { onClick() }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateAppearance()
    }

    private func updateAppearance() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            if isActive {
                layer?.backgroundColor = Theme.accent.cgColor
                iconView.contentTintColor = Theme.onAccent
                titleLabel.textColor = Theme.onAccent
            } else {
                layer?.backgroundColor = isHovered ? Theme.hoverWash.cgColor : NSColor.clear.cgColor
                iconView.contentTintColor = Theme.ink
                titleLabel.textColor = Theme.inkDim
            }
            needsDisplay = true
            layer?.setNeedsDisplay()
        }
    }
}

struct PreferencesView: NSViewControllerRepresentable {
    let model: SettingsModel

    func makeNSViewController(context: Context) -> PreferencesViewController {
        PreferencesViewController(model: model)
    }

    func updateNSViewController(_ nsViewController: PreferencesViewController, context: Context) {}
}

private struct PreferencesPane<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                content
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .font(PrefFont.body)
        .background(PrefColor.paper)
        .tint(PrefColor.accentDeep)
        .scrollIndicators(.hidden)
    }
}

// MARK: - Building blocks (dense rows, hairline separated)

private struct PrefSection<Content: View>: View {
    let title: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text(title.uppercased())
                .font(PrefFont.section)
                .foregroundStyle(PrefColor.inkDim)
            VStack(spacing: 0) { content }
                .background(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(PrefColor.chrome)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .stroke(PrefColor.hairline, lineWidth: 1)
                )
                .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
        }
    }
}

private struct PrefRow<Trailing: View>: View {
    let label: String
    var divider = true
    @ViewBuilder let trailing: Trailing

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(label)
                    .font(PrefFont.label)
                    .foregroundStyle(PrefColor.ink)
                Spacer()
                trailing
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 7)
            if divider {
                Rectangle()
                    .fill(PrefColor.hairline)
                    .frame(height: 1)
                    .padding(.leading, 12)
            }
        }
    }
}

private struct PrefCaption: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(PrefFont.monoSmall)
            .foregroundStyle(.tertiary)
            .padding(.top, 4)
    }
}

private struct PrefButtonStyle: ButtonStyle {
    enum Tone: Equatable { case secondary, destructive }

    var tone: Tone = .secondary

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 11.5, weight: .semibold))
            .lineLimit(1)
            .fixedSize(horizontal: true, vertical: false)
            .foregroundStyle(foreground)
            .padding(.horizontal, 9)
            .padding(.vertical, 4)
            .background(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(background(configuration.isPressed))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .stroke(border, lineWidth: 1)
            )
    }

    private var foreground: Color {
        tone == .destructive ? PrefColor.accent : PrefColor.ink
    }

    private var border: Color {
        tone == .destructive ? PrefColor.accent.opacity(0.45) : PrefColor.hairline
    }

    private func background(_ pressed: Bool) -> Color {
        if tone == .destructive {
            return PrefColor.accent.opacity(pressed ? 0.18 : 0.08)
        }
        return Color.primary.opacity(pressed ? 0.10 : 0.045)
    }
}

private struct PrefPopupOption<Value: Hashable> {
    let title: String
    let value: Value

    init(_ title: String, _ value: Value) {
        self.title = title
        self.value = value
    }
}

private struct PrefPopupPicker<Value: Hashable>: NSViewRepresentable {
    @Binding var selection: Value
    let options: [PrefPopupOption<Value>]

    func makeNSView(context: Context) -> NSPopUpButton {
        let button = NSPopUpButton(frame: .zero, pullsDown: false)
        button.controlSize = .small
        button.font = .systemFont(ofSize: NSFont.smallSystemFontSize)
        button.target = context.coordinator
        button.action = #selector(Coordinator.changed(_:))
        return button
    }

    func updateNSView(_ button: NSPopUpButton, context: Context) {
        context.coordinator.parent = self
        if button.numberOfItems != options.count || zip(button.itemTitles, options).contains(where: { $0 != $1.title }) {
            button.removeAllItems()
            button.addItems(withTitles: options.map(\.title))
        }
        if let index = options.firstIndex(where: { $0.value == selection }) {
            button.selectItem(at: index)
        }
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(parent: self)
    }

    final class Coordinator: NSObject {
        var parent: PrefPopupPicker

        init(parent: PrefPopupPicker) {
            self.parent = parent
        }

        @objc func changed(_ sender: NSPopUpButton) {
            let index = sender.indexOfSelectedItem
            guard parent.options.indices.contains(index) else { return }
            parent.selection = parent.options[index].value
        }
    }
}

private extension View {
    func prefTextField(width: CGFloat) -> some View {
        self
            .textFieldStyle(.plain)
            .font(PrefFont.mono)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .frame(width: width)
            .background(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .fill(PrefColor.field)
            )
            .overlay(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .stroke(PrefColor.hairline, lineWidth: 1)
            )
    }
}

// MARK: - General

struct GeneralTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "Appearance") {
            PrefRow(label: "Theme", divider: false) {
                Picker("", selection: themeModeBinding) {
                    Text("System").tag(ThemeMode.system)
                    Text("Light").tag(ThemeMode.light)
                    Text("Dark").tag(ThemeMode.dark)
                }
                .labelsHidden()
                .pickerStyle(.segmented)
                .controlSize(.small)
                .fixedSize()
            }
        }

        PrefSection(title: "Startup") {
            PrefRow(label: "Launch at login", divider: false) {
                Toggle("", isOn: launchAtLoginBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
        }

        PrefSection(title: "Reset") {
            PrefRow(label: "Restore every setting to its default", divider: false) {
                Button("Reset all", role: .destructive) {
                    model.update { $0 = .defaults }
                }
                .buttonStyle(PrefButtonStyle(tone: .destructive))
            }
        }
    }

    private var themeModeBinding: Binding<ThemeMode> {
        Binding(
            get: { model.settings.themeMode },
            set: { value in model.update { $0.themeMode = value } }
        )
    }

    private var launchAtLoginBinding: Binding<Bool> {
        Binding(
            get: { model.settings.launchAtLogin },
            set: { value in
                // Only effective when running as a bundled .app.
                do {
                    if value { try SMAppService.mainApp.register() }
                    else { try SMAppService.mainApp.unregister() }
                    model.update { $0.launchAtLogin = value }
                } catch {
                    NSLog("scrcap: launch-at-login toggle failed: \(error)")
                    let alert = NSAlert()
                    alert.messageText = "Could not update Launch at Login"
                    alert.informativeText = error.localizedDescription
                    alert.alertStyle = .warning
                    alert.runModal()
                    model.objectWillChange.send()
                }
            }
        )
    }
}

// MARK: - Capture

struct CaptureTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "After capture") {
            ForEach(Array(CaptureMode.captureOrder.enumerated()), id: \.element.rawValue) { index, mode in
                PrefRow(label: label(for: mode), divider: index < CaptureMode.captureOrder.count - 1) {
                    PrefPopupPicker(selection: binding(for: mode), options: [
                        PrefPopupOption("Open editor", .openEditor),
                        PrefPopupOption("Copy only", .copyOnly),
                        PrefPopupOption("Copy + editor", .both),
                    ])
                    .fixedSize()
                }
            }
        }

        PrefSection(title: "Capture options") {
            PrefRow(label: "Include pointer") {
                Toggle("", isOn: includeCursorBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
            PrefRow(label: "Delayed capture countdown", divider: false) {
                HStack(spacing: 4) {
                    TextField("", value: captureDelayBinding, format: .number)
                        .multilineTextAlignment(.trailing)
                        .prefTextField(width: 44)
                    Stepper("", value: captureDelayBinding, in: AppSettings.minCaptureDelay...AppSettings.maxCaptureDelay)
                        .labelsHidden()
                    Text("s").font(PrefFont.mono).foregroundStyle(.secondary)
                }
            }
        }

        PrefSection(title: "Window capture") {
            PrefRow(label: "Target window") {
                PrefPopupPicker(selection: windowTargetBinding, options: [
                    PrefPopupOption("Frontmost", .active),
                    PrefPopupOption("Pick on screen", .selected),
                ])
                .fixedSize()
            }
            PrefRow(label: "Include window shadow") {
                Toggle("", isOn: shadowBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
            PrefRow(label: "Transparent background") {
                Toggle("", isOn: windowTransparentBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
            PrefRow(label: "Background color", divider: false) {
                ColorPicker("", selection: windowBackgroundBinding, supportsOpacity: false)
                    .labelsHidden()
                    .disabled(model.settings.windowBackgroundTransparent)
            }
        }

        PrefSection(title: "Scrolling capture") {
            PrefRow(label: "Maximum stitched height", divider: false) {
                HStack(spacing: 4) {
                    TextField("", value: maxHeightBinding, format: .number)
                        .multilineTextAlignment(.trailing)
                        .prefTextField(width: 68)
                    Text("px")
                        .font(PrefFont.mono)
                        .foregroundStyle(.secondary)
                }
            }
        }
    }

    private var windowTargetBinding: Binding<WindowCaptureTarget> {
        Binding(
            get: { model.settings.windowCaptureTarget },
            set: { value in model.update { $0.windowCaptureTarget = value } }
        )
    }

    private var includeCursorBinding: Binding<Bool> {
        Binding(
            get: { model.settings.includeCursor },
            set: { value in model.update { $0.includeCursor = value } }
        )
    }

    private var captureDelayBinding: Binding<Int> {
        Binding(
            get: { model.settings.captureDelaySeconds },
            set: { value in model.update { $0.captureDelaySeconds = value } }
        )
    }

    private func label(for mode: CaptureMode) -> String {
        switch mode {
        case .fullscreen: return "Fullscreen"
        case .region: return "Region"
        case .window: return "Window"
        case .scrolling: return "Scrolling"
        }
    }

    private func binding(for mode: CaptureMode) -> Binding<AfterCaptureBehavior> {
        Binding(
            get: { model.settings.behavior(for: mode) },
            set: { value in model.update { $0.afterCapture[mode.rawValue] = value } }
        )
    }

    private var shadowBinding: Binding<Bool> {
        Binding(
            get: { model.settings.includeWindowShadow },
            set: { value in model.update { $0.includeWindowShadow = value } }
        )
    }

    private var windowTransparentBinding: Binding<Bool> {
        Binding(
            get: { model.settings.windowBackgroundTransparent },
            set: { value in model.update { $0.windowBackgroundTransparent = value } }
        )
    }

    private var windowBackgroundBinding: Binding<Color> {
        Binding(
            get: { Color(nsColor: NSColor(hex: model.settings.windowBackgroundHex) ?? .white) },
            set: { value in model.update { $0.windowBackgroundHex = NSColor(value).hexString } }
        )
    }

    private var maxHeightBinding: Binding<Int> {
        Binding(
            get: { model.settings.scrollingMaxHeight },
            set: { value in
                model.update {
                    $0.scrollingMaxHeight = min(
                        max(AppSettings.minScrollingMaxHeight, value),
                        AppSettings.maxScrollingMaxHeight
                    )
                }
            }
        )
    }
}

// MARK: - Shortcuts

struct ShortcutsTab: View {
    @ObservedObject var model: SettingsModel
    @State private var recordingAction: AppAction?
    @State private var warning: String?

    var body: some View {
        PrefSection(title: "Global capture shortcuts") {
            ForEach(Array(AppAction.shortcutOrder.enumerated()), id: \.element.rawValue) { index, action in
                PrefRow(label: action.title, divider: index < AppAction.shortcutOrder.count - 1) {
                    ShortcutRecorderButton(
                        chord: model.settings.keymap.chord(for: action),
                        isRecording: recordingAction == action,
                        onBeginRecording: { recordingAction = action },
                        onChord: { chord in record(chord, for: action) },
                        onCancel: { recordingAction = nil }
                    )
                }
            }
        }
        if let warning {
            Label(warning, systemImage: "exclamationmark.triangle.fill")
                .font(PrefFont.mono)
                .foregroundStyle(PrefColor.warn)
        }
        PrefCaption("click a shortcut, press any combination · esc cancels")
    }

    private func record(_ chord: KeyChord, for action: AppAction) {
        recordingAction = nil
        if Keymap.isSystemReserved(chord) {
            warning = "\(chord.displayValue) is a macOS system shortcut — pick another."
            return
        }
        var map = model.settings.keymap
        let stolen = map.set(chord, for: action)
        model.update { $0.apply(map) }
        warning = stolen.map { "\(chord.displayValue) was taken from “\($0.title)” — that action is now unbound." }
    }
}

/// AppKit-backed key recorder: a button that, while recording, swallows the
/// next keyDown via a local monitor and reports it as a KeyChord.
struct ShortcutRecorderButton: View {
    let chord: KeyChord?
    let isRecording: Bool
    let onBeginRecording: () -> Void
    let onChord: (KeyChord) -> Void
    let onCancel: () -> Void

    @State private var monitor: Any?

    var body: some View {
        Button {
            isRecording ? stopRecording(cancelled: true) : startRecording()
        } label: {
            Text(isRecording ? "RECORDING…" : (chord?.displayValue ?? "—"))
                .font(.system(size: 11.5, weight: .medium, design: .monospaced))
                .padding(.horizontal, 10)
                .padding(.vertical, 3)
                .frame(minWidth: 72)
                .foregroundStyle(isRecording ? PrefColor.onAccent : PrefColor.ink)
                .background(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(isRecording ? PrefColor.accent : Color.primary.opacity(0.045))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .stroke(isRecording ? PrefColor.accentDeep : PrefColor.hairline, lineWidth: 1)
                )
        }
        .buttonStyle(.plain)
        .onDisappear { stopRecording(cancelled: true) }
    }

    private func startRecording() {
        onBeginRecording()
        NotificationCenter.default.post(name: .scrcapShortcutRecording, object: true)
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 53 { // esc cancels
                stopRecording(cancelled: true)
                return nil
            }
            if let chord = KeyCodeMap.chord(from: event), !chord.modifiers.isEmpty {
                stopRecording(cancelled: false)
                onChord(chord)
                return nil
            }
            return nil // swallow modifier-less keys while recording
        }
    }

    private func stopRecording(cancelled: Bool) {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil
        NotificationCenter.default.post(name: .scrcapShortcutRecording, object: false)
        if cancelled { onCancel() }
    }
}

// MARK: - Editor

struct EditorTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "Palette") {
            PrefRow(label: "Tool colors", divider: false) {
                HStack(spacing: 8) {
                    ForEach(0..<AppSettings.paletteSlotCount, id: \.self) { index in
                        paletteSlot(index)
                    }
                    Button("Reset") {
                        model.update { $0.paletteHex = AppSettings.defaultPalette }
                    }
                    .buttonStyle(PrefButtonStyle())
                }
            }
        }
        PrefCaption("keys 1–5 select colors · slot 1 is the default for each new capture")

        PrefSection(title: "Drawing") {
            PrefRow(label: "Default stroke width", divider: false) {
                HStack(spacing: 8) {
                    Slider(
                        value: strokeBinding,
                        in: AppSettings.minStrokeWidth...AppSettings.maxStrokeWidth,
                        step: 0.5
                    )
                        .controlSize(.small)
                        .frame(width: 128)
                    Text(String(format: "%.1f pt", model.settings.strokeWidth))
                        .font(PrefFont.mono)
                        .foregroundStyle(.secondary)
                        .frame(width: 44, alignment: .trailing)
                }
            }
        }

        PrefSection(title: "Text") {
            PrefRow(label: "Default text size") {
                HStack(spacing: 8) {
                    Slider(
                        value: textSizeBinding,
                        in: AppSettings.minTextSize...AppSettings.maxTextSize,
                        step: 1
                    )
                        .controlSize(.small)
                        .frame(width: 128)
                    Text(String(format: "%.0f pt", model.settings.textSize))
                        .font(PrefFont.mono)
                        .foregroundStyle(.secondary)
                        .frame(width: 44, alignment: .trailing)
                }
            }
            PrefRow(label: "Return while typing", divider: false) {
                PrefPopupPicker(selection: textEnterBinding, options: [
                    PrefPopupOption("New line", .newline),
                    PrefPopupOption("Commit text", .commit),
                ])
                .fixedSize()
            }
        }

        PrefSection(title: "Canvas") {
            PrefRow(label: "Auto-expand while drawing") {
                Toggle("", isOn: autoExpandCanvasBinding)
                    .labelsHidden()
                    .toggleStyle(.switch)
                    .controlSize(.mini)
            }
            PrefRow(label: "New area fill", divider: false) {
                ColorPicker("", selection: canvasExtensionColorBinding, supportsOpacity: false)
                    .labelsHidden()
                    .disabled(!model.settings.autoExpandCanvas)
            }
        }

        PrefSection(title: "Close behavior") {
            PrefRow(label: "Esc key", divider: false) {
                PrefPopupPicker(selection: escBinding, options: [
                    PrefPopupOption("Copy + close", .copyAndClose),
                    PrefPopupOption("Close only", .closeOnly),
                ])
                .fixedSize()
            }
        }
    }

    private func paletteBinding(_ index: Int) -> Binding<Color> {
        Binding(
            get: { Color(nsColor: NSColor(hex: model.settings.paletteHex[index]) ?? .systemRed) },
            set: { value in
                model.update { $0.paletteHex[index] = NSColor(value).hexString }
            }
        )
    }

    private func paletteSlot(_ index: Int) -> some View {
        VStack(spacing: 2) {
            ColorPicker("", selection: paletteBinding(index), supportsOpacity: false)
                .labelsHidden()
            Text("\(index + 1)")
                .font(PrefFont.monoSmall)
                .foregroundStyle(.tertiary)
        }
    }

    private var escBinding: Binding<EscBehavior> {
        Binding(
            get: { model.settings.escBehavior },
            set: { value in model.update { $0.escBehavior = value } }
        )
    }

    private var strokeBinding: Binding<Double> {
        Binding(
            get: { model.settings.strokeWidth },
            set: { value in model.update { $0.strokeWidth = value } }
        )
    }

    private var textSizeBinding: Binding<Double> {
        Binding(
            get: { model.settings.textSize },
            set: { value in model.update { $0.textSize = value } }
        )
    }

    private var textEnterBinding: Binding<TextEnterBehavior> {
        Binding(
            get: { model.settings.textEnterBehavior },
            set: { value in model.update { $0.textEnterBehavior = value } }
        )
    }

    private var autoExpandCanvasBinding: Binding<Bool> {
        Binding(
            get: { model.settings.autoExpandCanvas },
            set: { value in model.update { $0.autoExpandCanvas = value } }
        )
    }

    private var canvasExtensionColorBinding: Binding<Color> {
        Binding(
            get: { Color(nsColor: NSColor(hex: model.settings.canvasExtensionBackgroundHex) ?? .white) },
            set: { value in model.update { $0.canvasExtensionBackgroundHex = NSColor(value).hexString } }
        )
    }

}

// MARK: - Output

struct OutputTab: View {
    @ObservedObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "Files") {
            PrefRow(label: "Save folder") {
                HStack(spacing: 6) {
                    TextField("~/Desktop", text: saveFolderBinding)
                        .prefTextField(width: 150)
                    Button("Choose…") { chooseSaveFolder() }
                        .buttonStyle(PrefButtonStyle())
                }
            }
            PrefRow(label: "Filename pattern", divider: false) {
                TextField("", text: patternBinding)
                    .prefTextField(width: 200)
            }
        }
        PrefCaption("tokens: {date} {time} · ⌘S saves here · ⇧⌘S to choose each time")

        PrefSection(title: "Image export") {
            PrefRow(label: "PNG scale", divider: false) {
                Picker("", selection: exportScaleBinding) {
                    Text("1× points").tag(1)
                    Text("2× Retina").tag(2)
                }
                .labelsHidden()
                .pickerStyle(.segmented)
                .controlSize(.small)
                .fixedSize()
            }
        }

        PrefSection(title: "Feedback") {
            PrefRow(label: "Notify when copied to clipboard", divider: false) {
                Toggle("", isOn: copyNotificationBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
        }
        PrefCaption("settings: ~/Library/Application Support/scrcap/settings.json — human-readable on purpose")
    }

    // Stored inverted (suppress…) so the default of all-zero settings = notifications on.
    private var copyNotificationBinding: Binding<Bool> {
        Binding(
            get: { !model.settings.suppressCopyNotification },
            set: { value in model.update { $0.suppressCopyNotification = !value } }
        )
    }

    private func chooseSaveFolder() {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.allowsMultipleSelection = false
        panel.directoryURL = Exporter.defaultSaveFolder(settings: model.settings)
        if panel.runModal() == .OK, let url = panel.url {
            model.update { $0.saveFolder = url.path }
        }
    }

    private var saveFolderBinding: Binding<String> {
        Binding(
            get: { model.settings.saveFolder ?? "" },
            set: { value in model.update { $0.saveFolder = value.isEmpty ? nil : value } }
        )
    }

    private var patternBinding: Binding<String> {
        Binding(
            get: { model.settings.filenamePattern },
            set: { value in model.update { $0.filenamePattern = value } }
        )
    }

    private var exportScaleBinding: Binding<Int> {
        Binding(
            get: { model.settings.exportScale },
            set: { value in model.update { $0.exportScale = value } }
        )
    }
}

// MARK: - About

struct AboutTab: View {
    private var version: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 8) {
                if let logo = Theme.logoImage(size: 22) {
                    Image(nsImage: logo)
                        .resizable()
                        .renderingMode(.template)
                        .frame(width: 20, height: 20)
                        .foregroundStyle(PrefColor.accent)
                }
                Text("SCRCAP")
                    .font(.system(size: 20, weight: .black))
            }
            Text("v\(version) — fast, keyboard-first screenshots")
                .font(PrefFont.mono)
                .foregroundStyle(.secondary)
        }
        .padding(.bottom, 2)

        PrefSection(title: "Links") {
            PrefRow(label: "Created by") {
                Link("kubre.in ↗", destination: URL(string: "https://kubre.in")!)
                    .font(PrefFont.label)
                    .underline()
            }
            PrefRow(label: "Source") {
                Link("github.com/kubre/scrcap ↗", destination: URL(string: "https://github.com/kubre/scrcap")!)
                    .font(PrefFont.label)
                    .underline()
            }
            PrefRow(label: "Updates") {
                Button("Check for Updates…") {
                    NotificationCenter.default.post(name: .scrcapCheckForUpdates, object: nil)
                }
                .buttonStyle(PrefButtonStyle())
            }
        }
        VStack(alignment: .leading, spacing: 4) {
            PrefCaption("this app is inspired by Shottr but has much more opinionated defaults. if you need an app with more features, use Shottr instead.")
            Link("shottr.cc ↗", destination: URL(string: "https://shottr.cc")!)
                .font(PrefFont.mono)
                .fontWeight(.medium)
                .underline()
                .foregroundStyle(Color.primary.opacity(0.7))
        }
        .padding(.top, -10)
    }
}
