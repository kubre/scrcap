// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "scrcap",
    platforms: [.macOS(.v14)],
    targets: [
        // Portable core — no AppKit, no CoreGraphics types. See docs/core-model.md.
        .target(
            name: "ScrcapCore",
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        .executableTarget(
            name: "scrcap",
            dependencies: ["ScrcapCore"],
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
        // Plain-executable test runner: the toolchain here is CLT-only, which
        // ships neither XCTest nor Swift Testing. Run with:
        //   swift run scrcap-core-tests
        .executableTarget(
            name: "scrcap-core-tests",
            dependencies: ["ScrcapCore"],
            swiftSettings: [.swiftLanguageMode(.v5)]
        ),
    ]
)
