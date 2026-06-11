// HotkeyService — Carbon RegisterEventHotKey wrapper. Still the only API
// delivering reliable system-wide hotkeys without Accessibility permission
// or an event tap. Re-registers live on remap.

import Carbon.HIToolbox
import AppKit
import ScrcapCore

final class HotkeyService {
    var onAction: ((AppAction) -> Void)?

    private var hotkeyRefs: [EventHotKeyRef] = []
    private var idToAction: [UInt32: AppAction] = [:]
    private var eventHandler: EventHandlerRef?
    private var nextID: UInt32 = 1
    private static let signature: OSType = 0x53435243 // 'SCRC'

    init() {
        installHandler()
    }

    func register(keymap: Keymap) {
        unregisterAll()
        for (action, chord) in keymap.bindings {
            guard let keyCode = KeyCodeMap.carbonKeyCode(for: chord) else {
                NSLog("scrcap: unmappable chord \(chord.stringValue) for \(action.rawValue)")
                continue
            }
            let id = nextID
            nextID += 1
            var ref: EventHotKeyRef?
            let hotkeyID = EventHotKeyID(signature: HotkeyService.signature, id: id)
            let status = RegisterEventHotKey(
                keyCode,
                KeyCodeMap.carbonModifiers(for: chord),
                hotkeyID,
                GetEventDispatcherTarget(),
                0,
                &ref
            )
            if status == noErr, let ref {
                hotkeyRefs.append(ref)
                idToAction[id] = action
            } else {
                NSLog("scrcap: failed to register \(chord.displayValue) (\(status))")
            }
        }
    }

    private func unregisterAll() {
        for ref in hotkeyRefs { UnregisterEventHotKey(ref) }
        hotkeyRefs.removeAll()
        idToAction.removeAll()
    }

    private func installHandler() {
        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )
        InstallEventHandler(
            GetEventDispatcherTarget(),
            { _, eventRef, userData -> OSStatus in
                guard let eventRef, let userData else { return noErr }
                var hotkeyID = EventHotKeyID()
                GetEventParameter(
                    eventRef,
                    EventParamName(kEventParamDirectObject),
                    EventParamType(typeEventHotKeyID),
                    nil,
                    MemoryLayout<EventHotKeyID>.size,
                    nil,
                    &hotkeyID
                )
                let service = Unmanaged<HotkeyService>.fromOpaque(userData).takeUnretainedValue()
                service.fire(id: hotkeyID.id)
                return noErr
            },
            1,
            &eventType,
            Unmanaged.passUnretained(self).toOpaque(),
            &eventHandler
        )
    }

    private func fire(id: UInt32) {
        guard let action = idToAction[id] else { return }
        onAction?(action)
    }

    deinit {
        unregisterAll()
        if let eventHandler { RemoveEventHandler(eventHandler) }
    }
}
