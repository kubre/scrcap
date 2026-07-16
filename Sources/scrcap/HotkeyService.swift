// HotkeyService — Carbon RegisterEventHotKey wrapper. Still the only API
// delivering reliable system-wide hotkeys without Accessibility permission
// or an event tap. Re-registers live on remap.

import Carbon.HIToolbox
import AppKit
import ScrcapCore

struct HotkeyRegistrationFailure: Equatable {
    let action: AppAction
    let chord: KeyChord
    let status: OSStatus
}

struct HotkeyRegistrationResult {
    let activeKeymap: Keymap
    let failures: [HotkeyRegistrationFailure]

    var succeeded: Bool { failures.isEmpty }
}

final class HotkeyService {
    var onAction: ((AppAction) -> Void)?

    private var hotkeyRefs: [EventHotKeyRef] = []
    private var idToAction: [UInt32: AppAction] = [:]
    private var suspendedKeymap: Keymap?
    private var eventHandler: EventHandlerRef?
    private var nextID: UInt32 = 1
    private static let signature: OSType = 0x53435243 // 'SCRC'

    init() {
        installHandler()
    }

    @discardableResult
    func register(keymap: Keymap) -> HotkeyRegistrationResult {
        if suspendedKeymap != nil {
            suspendedKeymap = keymap
            return HotkeyRegistrationResult(activeKeymap: Keymap(bindings: [:]), failures: [])
        }
        let previousKeymap = activeKeymap
        if keymap == previousKeymap {
            return HotkeyRegistrationResult(activeKeymap: previousKeymap, failures: [])
        }
        unregisterAll()
        let failures = registerBindings(keymap)
        guard failures.isEmpty else {
            if previousKeymap.bindings.isEmpty {
                return HotkeyRegistrationResult(activeKeymap: activeKeymap, failures: failures)
            }
            unregisterAll()
            let rollbackFailures = registerBindings(previousKeymap)
            return HotkeyRegistrationResult(
                activeKeymap: activeKeymap,
                failures: failures + rollbackFailures
            )
        }
        return HotkeyRegistrationResult(activeKeymap: activeKeymap, failures: [])
    }

    @discardableResult
    func setSuspended(_ suspended: Bool) -> HotkeyRegistrationResult {
        if suspended {
            guard suspendedKeymap == nil else {
                return HotkeyRegistrationResult(activeKeymap: activeKeymap, failures: [])
            }
            suspendedKeymap = activeKeymap
            unregisterAll()
            return HotkeyRegistrationResult(activeKeymap: activeKeymap, failures: [])
        }
        guard let keymap = suspendedKeymap else {
            return HotkeyRegistrationResult(activeKeymap: activeKeymap, failures: [])
        }
        suspendedKeymap = nil
        return register(keymap: keymap)
    }

    private var activeKeymap: Keymap {
        Keymap(bindings: idToAction.reduce(into: [:]) { bindings, entry in
            guard let chord = idToChord[entry.key] else { return }
            bindings[entry.value] = chord
        })
    }

    private var idToChord: [UInt32: KeyChord] = [:]

    private func registerBindings(_ keymap: Keymap) -> [HotkeyRegistrationFailure] {
        var failures: [HotkeyRegistrationFailure] = []
        for (action, chord) in keymap.bindings {
            guard let keyCode = KeyCodeMap.carbonKeyCode(for: chord) else {
                NSLog("scrcap: unmappable chord \(chord.stringValue) for \(action.rawValue)")
                failures.append(HotkeyRegistrationFailure(action: action, chord: chord, status: OSStatus(paramErr)))
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
                idToChord[id] = chord
            } else {
                NSLog("scrcap: failed to register \(chord.displayValue) (\(status))")
                failures.append(HotkeyRegistrationFailure(action: action, chord: chord, status: status))
            }
        }
        return failures
    }

    private func unregisterAll() {
        for ref in hotkeyRefs { UnregisterEventHotKey(ref) }
        hotkeyRefs.removeAll()
        idToAction.removeAll()
        idToChord.removeAll()
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
