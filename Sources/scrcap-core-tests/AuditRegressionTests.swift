import Foundation
import ScrcapCore

func runAuditRegressionTests() {
    print("\nAudit regressions")

    test("counter numbering does not reuse a surviving number after cropping") {
        var stack = AnnotationStack()
        stack.append(Shape(kind: .counter(number: 2), colorIndex: 0,
                           start: CorePoint(x: 0, y: 0), end: CorePoint(x: 0, y: 0)))
        checkEqual(stack.nextCounterNumber, 3)
        stack.undo()
        checkEqual(stack.nextCounterNumber, 1)
        stack.redo()
        checkEqual(stack.nextCounterNumber, 3)
    }

    test("settings reject zero and negative schema versions before migration") {
        for version in [0, -1, Int.min] {
            let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            defer { try? FileManager.default.removeItem(at: directory) }
            let data = Data("{\"schemaVersion\":\(version)}".utf8)
            try data.write(to: directory.appendingPathComponent("settings.json"))
            checkEqual(SettingsStore(directory: directory).settings, .defaults)
        }
    }

    test("current settings retain deliberately reordered capture shortcuts") {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let store = SettingsStore(directory: directory)
        check(store.update {
            $0.hotkeys[AppAction.captureWindow.rawValue] = "opt+shift+3"
            $0.hotkeys[AppAction.captureFullscreen.rawValue] = "opt+shift+2"
        })
        checkEqual(store.settings.hotkeys[AppAction.captureWindow.rawValue], "opt+shift+3")
        let reloaded = SettingsStore(directory: directory)
        checkEqual(reloaded.settings, store.settings)
    }

    test("exact alignment rejects invalid arguments without trapping") {
        let rows: [UInt64] = [1, 2, 3]
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, minOverlap: -1))
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, minOverlap: 0))
        for tolerance in [Double.nan, .infinity, -0.1, 1.1] {
            checkNil(StitchEngine.align(accumulated: rows, frame: rows, tolerance: tolerance))
        }
    }

    test("perceptual alignment rejects invalid arguments without trapping") {
        let rows = [StitchEngine.RowSignature(bins: [0, 255])]
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, minOverlap: -1))
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, rowTolerance: .nan))
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, requiredMatchRatio: 2))
        checkNil(StitchEngine.align(accumulated: rows, frame: rows, maximumMeanDistance: -.infinity))
    }

    test("unique writer preserves existing content and publishes complete files") {
        let manager = FileManager.default
        let directory = manager.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try manager.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? manager.removeItem(at: directory) }
        let first = try UniqueFileWriter.write(Data("first".utf8), directory: directory, filename: "shot.png")
        let second = try UniqueFileWriter.write(Data("second".utf8), directory: directory, filename: "shot.png")
        checkEqual(first.lastPathComponent, "shot.png")
        checkEqual(second.lastPathComponent, "shot-2.png")
        checkEqual(try Data(contentsOf: first), Data("first".utf8))
        checkEqual(try Data(contentsOf: second), Data("second".utf8))
        checkEqual(try manager.contentsOfDirectory(atPath: directory.path).count, 2)
    }

    test("concurrent unique saves never replace another payload") {
        let manager = FileManager.default
        let directory = manager.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try manager.createDirectory(at: directory, withIntermediateDirectories: true)
        defer { try? manager.removeItem(at: directory) }
        let lock = NSLock()
        var saved: [URL: Data] = [:]
        var errors: [String] = []
        // Contention fixture, not a production concurrency limit.
        DispatchQueue.concurrentPerform(iterations: 16) { index in
            let data = Data("capture-\(index)".utf8)
            do {
                let url = try UniqueFileWriter.write(data, directory: directory, filename: "shot.png")
                lock.lock()
                if saved[url] != nil { errors.append("Duplicate URL: \(url.path)") }
                saved[url] = data
                lock.unlock()
            } catch {
                lock.lock()
                errors.append(String(describing: error))
                lock.unlock()
            }
        }
        check(errors.isEmpty, errors.joined(separator: "; "))
        checkEqual(saved.count, 16)
        for (url, expected) in saved { checkEqual(try Data(contentsOf: url), expected) }
    }
}
