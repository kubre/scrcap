// Preferences — SwiftUI is allowed here (plan §04): latency is irrelevant and
// the frameworks ship with macOS. Same design language as the editor: dense,
// red primary, monospaced shortcut hints, follows the system theme.

import AppKit
import SwiftUI
import ServiceManagement
import ScrcapCore

// SwiftUI declares its own `Settings` scene type; disambiguate.
typealias AppSettings = ScrcapCore.Settings

extension Notification.Name {
    static let scrcapSettingsChanged = Notification.Name("scrcapSettingsChanged")
    /// object: Bool — true while the shortcut recorder is armed. Global
    /// hotkeys pause so pressing a currently-bound chord re-records it
    /// instead of firing a capture.
    static let scrcapShortcutRecording = Notification.Name("scrcapShortcutRecording")
}

private let scrcapRed = Color(red: 1.0, green: 0.23, blue: 0.19) // #FF3B30

final class SettingsModel: ObservableObject {
    let store: SettingsStore

    init(store: SettingsStore) {
        self.store = store
    }

    var settings: AppSettings { store.settings }

    func update(_ mutate: (inout AppSettings) -> Void) {
        objectWillChange.send()
        store.update(mutate)
        NotificationCenter.default.post(name: .scrcapSettingsChanged, object: nil)
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
            let hosting = NSHostingController(rootView: PreferencesView().environmentObject(model))
            let w = NSWindow(contentViewController: hosting)
            w.title = "scrcap"
            w.styleMask = [.titled, .closable]
            w.titlebarAppearsTransparent = true
            w.isReleasedWhenClosed = false
            window = w
        }
        NSApp.activate(ignoringOtherApps: true)
        window?.center()
        window?.makeKeyAndOrderFront(nil)
    }
}

// MARK: - Root

private enum PrefTab: String, CaseIterable {
    case general = "General"
    case shortcuts = "Shortcuts"
    case editor = "Editor"
    case advanced = "Advanced"
    case about = "About"

    var icon: String {
        switch self {
        case .general: return "gearshape"
        case .shortcuts: return "keyboard"
        case .editor: return "paintbrush"
        case .advanced: return "wrench.and.screwdriver"
        case .about: return "info.circle"
        }
    }
}

struct PreferencesView: View {
    @State private var tab: PrefTab = .general

    var body: some View {
        VStack(spacing: 0) {
            tabBar
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    switch tab {
                    case .general: GeneralTab()
                    case .shortcuts: ShortcutsTab()
                    case .editor: EditorTab()
                    case .advanced: AdvancedTab()
                    case .about: AboutTab()
                    }
                }
                .padding(20)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .frame(width: 480, height: 460)
        .tint(scrcapRed)
    }

    private var tabBar: some View {
        HStack(spacing: 6) {
            ForEach(PrefTab.allCases, id: \.rawValue) { item in
                Button {
                    tab = item
                } label: {
                    HStack(spacing: 5) {
                        Image(systemName: item.icon).font(.system(size: 11, weight: .medium))
                        Text(item.rawValue).font(.system(size: 11.5, weight: .medium))
                    }
                    .padding(.horizontal, 10)
                    .padding(.vertical, 5)
                    .foregroundStyle(tab == item ? scrcapRed : Color.secondary)
                    .background(
                        RoundedRectangle(cornerRadius: 6)
                            .fill(tab == item ? scrcapRed.opacity(0.14) : .clear)
                    )
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .strokeBorder(tab == item ? scrcapRed.opacity(0.55) : .clear, lineWidth: 1)
                    )
                }
                .buttonStyle(.plain)
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

// MARK: - Building blocks (dense rows, hairline separated)

private struct PrefSection<Content: View>: View {
    let title: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text(title.uppercased())
                .font(.system(size: 10, weight: .semibold, design: .monospaced))
                .kerning(1.2)
                .foregroundStyle(.secondary)
                .padding(.bottom, 6)
            VStack(spacing: 0) { content }
                .background(RoundedRectangle(cornerRadius: 8).fill(.quinary.opacity(0.5)))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(.separator, lineWidth: 1))
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
                Text(label).font(.system(size: 12.5))
                Spacer()
                trailing
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 7)
            if divider { Divider().padding(.leading, 12) }
        }
    }
}

private struct PrefCaption: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.system(size: 10.5, design: .monospaced))
            .foregroundStyle(.tertiary)
            .padding(.top, 4)
    }
}

// MARK: - General

struct GeneralTab: View {
    @EnvironmentObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "After capture") {
            ForEach(Array(CaptureMode.captureOrder.enumerated()), id: \.element.rawValue) { index, mode in
                PrefRow(label: label(for: mode), divider: index < CaptureMode.captureOrder.count - 1) {
                    Picker("", selection: binding(for: mode)) {
                        Text("Open editor").tag(AfterCaptureBehavior.openEditor)
                        Text("Copy only").tag(AfterCaptureBehavior.copyOnly)
                        Text("Copy + editor").tag(AfterCaptureBehavior.both)
                    }
                    .labelsHidden()
                    .controlSize(.small)
                    .fixedSize()
                }
            }
        }

        PrefSection(title: "Saving") {
            PrefRow(label: "Save folder") {
                TextField("~/Desktop", text: saveFolderBinding)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.small)
                    .font(.system(size: 11, design: .monospaced))
                    .frame(width: 200)
            }
            PrefRow(label: "Filename pattern", divider: false) {
                TextField("", text: patternBinding)
                    .textFieldStyle(.roundedBorder)
                    .controlSize(.small)
                    .font(.system(size: 11, design: .monospaced))
                    .frame(width: 200)
            }
        }
        PrefCaption("tokens: {date} {time} — e.g. scrcap-2026-06-11-14.32.05.png")

        PrefSection(title: "Startup") {
            PrefRow(label: "Launch at login", divider: false) {
                Toggle("", isOn: launchAtLoginBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
            }
        }
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

    private var launchAtLoginBinding: Binding<Bool> {
        Binding(
            get: { model.settings.launchAtLogin },
            set: { value in
                model.update { $0.launchAtLogin = value }
                // Only effective when running as a bundled .app.
                do {
                    if value { try SMAppService.mainApp.register() }
                    else { try SMAppService.mainApp.unregister() }
                } catch {
                    NSLog("scrcap: launch-at-login toggle failed: \(error)")
                }
            }
        )
    }
}

// MARK: - Shortcuts

struct ShortcutsTab: View {
    @EnvironmentObject var model: SettingsModel
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
                .font(.system(size: 11))
                .foregroundStyle(scrcapRed)
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
            Text(isRecording ? "recording…" : (chord?.displayValue ?? "—"))
                .font(.system(size: 11.5, weight: .medium, design: .monospaced))
                .padding(.horizontal, 10)
                .padding(.vertical, 3)
                .frame(minWidth: 72)
                .foregroundStyle(isRecording ? scrcapRed : .primary)
                .background(
                    RoundedRectangle(cornerRadius: 5)
                        .fill(isRecording ? scrcapRed.opacity(0.14) : Color.primary.opacity(0.06))
                )
                .overlay(
                    RoundedRectangle(cornerRadius: 5)
                        .strokeBorder(isRecording ? scrcapRed.opacity(0.55) : Color.primary.opacity(0.12), lineWidth: 1)
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
    @EnvironmentObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "Palette · keys 1–7 · slot 1 is the boot default") {
            PrefRow(label: "", divider: false) {
                HStack(spacing: 10) {
                    ForEach(0..<7, id: \.self) { index in
                        VStack(spacing: 2) {
                            ColorPicker("", selection: paletteBinding(index), supportsOpacity: false)
                                .labelsHidden()
                            Text("\(index + 1)")
                                .font(.system(size: 9, design: .monospaced))
                                .foregroundStyle(.tertiary)
                        }
                    }
                    Spacer()
                    Button("Reset") {
                        model.update { $0.paletteHex = AppSettings.defaultPalette }
                    }
                    .controlSize(.small)
                }
            }
        }

        PrefSection(title: "Behavior") {
            PrefRow(label: "Esc key") {
                Picker("", selection: escBinding) {
                    Text("Copy + close").tag(EscBehavior.copyAndClose)
                    Text("Close only").tag(EscBehavior.closeOnly)
                }
                .labelsHidden()
                .controlSize(.small)
                .fixedSize()
            }
            PrefRow(label: "Stroke width") {
                HStack(spacing: 8) {
                    Slider(value: strokeBinding, in: 1...8, step: 0.5)
                        .controlSize(.small)
                        .frame(width: 140)
                    Text(String(format: "%.1f pt", model.settings.strokeWidth))
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .frame(width: 44, alignment: .trailing)
                }
            }
            PrefRow(label: "Text size") {
                HStack(spacing: 8) {
                    Slider(value: textSizeBinding, in: 10...36, step: 1)
                        .controlSize(.small)
                        .frame(width: 140)
                    Text(String(format: "%.0f pt", model.settings.textSize))
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundStyle(.secondary)
                        .frame(width: 44, alignment: .trailing)
                }
            }
            PrefRow(label: "Return key") {
                Picker("", selection: textEnterBinding) {
                    Text("New line").tag(TextEnterBehavior.newline)
                    Text("Commit text").tag(TextEnterBehavior.commit)
                }
                .labelsHidden()
                .controlSize(.small)
                .fixedSize()
            }
            PrefRow(label: "Window capture includes shadow", divider: false) {
                Toggle("", isOn: shadowBinding).labelsHidden().toggleStyle(.switch).controlSize(.mini)
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

    private var shadowBinding: Binding<Bool> {
        Binding(
            get: { model.settings.includeWindowShadow },
            set: { value in model.update { $0.includeWindowShadow = value } }
        )
    }
}

// MARK: - Advanced

struct AdvancedTab: View {
    @EnvironmentObject var model: SettingsModel

    var body: some View {
        PrefSection(title: "Output") {
            PrefRow(label: "Export scale") {
                Picker("", selection: exportScaleBinding) {
                    Text("1× points").tag(1)
                    Text("2× Retina").tag(2)
                }
                .labelsHidden()
                .pickerStyle(.segmented)
                .controlSize(.small)
                .fixedSize()
            }
            PrefRow(label: "Scrolling capture max height", divider: false) {
                HStack(spacing: 4) {
                    TextField("", value: maxHeightBinding, format: .number)
                        .textFieldStyle(.roundedBorder)
                        .controlSize(.small)
                        .font(.system(size: 11, design: .monospaced))
                        .frame(width: 70)
                        .multilineTextAlignment(.trailing)
                    Text("px")
                        .font(.system(size: 11, design: .monospaced))
                        .foregroundStyle(.secondary)
                }
            }
        }

        PrefSection(title: "Reset") {
            PrefRow(label: "Restore every setting to its default", divider: false) {
                Button("Reset all", role: .destructive) {
                    model.update { $0 = .defaults }
                }
                .controlSize(.small)
            }
        }
        PrefCaption("settings: ~/Library/Application Support/scrcap/settings.json — human-readable on purpose")
    }

    private var exportScaleBinding: Binding<Int> {
        Binding(
            get: { model.settings.exportScale },
            set: { value in model.update { $0.exportScale = value } }
        )
    }

    private var maxHeightBinding: Binding<Int> {
        Binding(
            get: { model.settings.scrollingMaxHeight },
            set: { value in model.update { $0.scrollingMaxHeight = max(1000, value) } }
        )
    }
}

// MARK: - About

struct AboutTab: View {
    var body: some View {
        PrefSection(title: "About scrcap") {
            VStack(alignment: .leading, spacing: 8) {
                Text("scrcap is inspired by Shottr's fast, keyboard-first screenshot workflow.")
                    .font(.system(size: 12.5))
                Link("Visit Shottr", destination: URL(string: "https://shottr.cc")!)
                    .font(.system(size: 12.5, weight: .medium))
                Text("If you want a screenshot app with more configuration and advanced features, use Shottr instead.")
                    .font(.system(size: 12.5))
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 10)
        }
    }
}
