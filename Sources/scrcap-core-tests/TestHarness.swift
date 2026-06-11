// Minimal test harness — the CLT-only toolchain has no XCTest/Swift Testing.

import Foundation

final class TestRun {
    static let shared = TestRun()
    var passed = 0
    var failed = 0
    private var currentTest = ""

    func test(_ name: String, _ body: () throws -> Void) {
        currentTest = name
        let before = failed
        do {
            try body()
        } catch {
            fail("threw \(error)")
        }
        if failed == before {
            passed += 1
            print("  ✓ \(name)")
        }
    }

    func fail(_ message: String, line: UInt = #line) {
        failed += 1
        print("  ✗ \(currentTest): \(message) (line \(line))")
    }

    func finish() -> Never {
        print("\n\(passed) passed, \(failed) failed")
        exit(failed == 0 ? 0 : 1)
    }
}

func test(_ name: String, _ body: () throws -> Void) {
    TestRun.shared.test(name, body)
}

func check(_ condition: Bool, _ message: String = "expected true", line: UInt = #line) {
    if !condition { TestRun.shared.fail(message, line: line) }
}

func checkEqual<T: Equatable>(_ a: T, _ b: T, line: UInt = #line) {
    if a != b { TestRun.shared.fail("\(a) != \(b)", line: line) }
}

func checkNil<T>(_ value: T?, line: UInt = #line) {
    if let value { TestRun.shared.fail("expected nil, got \(value)", line: line) }
}
