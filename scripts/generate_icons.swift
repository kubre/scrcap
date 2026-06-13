#!/usr/bin/env swift
import AppKit
import CoreGraphics
import Foundation

let root = URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
let resources = root.appendingPathComponent("Sources/scrcap/Resources", isDirectory: true)
try FileManager.default.createDirectory(at: resources, withIntermediateDirectories: true)

func color(_ hex: UInt32, alpha: CGFloat = 1) -> CGColor {
    CGColor(
        red: CGFloat((hex >> 16) & 0xff) / 255,
        green: CGFloat((hex >> 8) & 0xff) / 255,
        blue: CGFloat(hex & 0xff) / 255,
        alpha: alpha
    )
}

func appIcon(size: Int, insetScale: CGFloat, cornerScale: CGFloat) -> NSImage {
    let bounds = CGRect(x: 0, y: 0, width: size, height: size)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: size,
        pixelsHigh: size,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    )!
    bitmap.size = NSSize(width: size, height: size)

    let context = NSGraphicsContext(bitmapImageRep: bitmap)!.cgContext
    context.setAllowsAntialiasing(true)
    context.setShouldAntialias(true)
    context.clear(bounds)

    let inset = CGFloat(size) * insetScale
    let iconRect = bounds.insetBy(dx: inset, dy: inset)
    let radius = CGFloat(size) * cornerScale
    let path = CGPath(roundedRect: iconRect, cornerWidth: radius, cornerHeight: radius, transform: nil)

    context.saveGState()
    context.setShadow(offset: CGSize(width: 0, height: -CGFloat(size) * 0.025), blur: CGFloat(size) * 0.055, color: color(0x660214, alpha: 0.30))
    context.addPath(path)
    context.clip()

    let gradient = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: [
        color(0xFF4D57), // red highlight
        color(0xE30E20), // theme accent
        color(0xA80513), // deep red
    ] as CFArray, locations: [0, 0.45, 1])!
    context.drawLinearGradient(
        gradient,
        start: CGPoint(x: iconRect.minX, y: iconRect.maxY),
        end: CGPoint(x: iconRect.maxX, y: iconRect.minY),
        options: []
    )

    context.addPath(path)
    context.setStrokeColor(color(0x000000, alpha: 0.12))
    context.setLineWidth(max(1, CGFloat(size) * 0.018))
    context.strokePath()
    context.restoreGState()

    let plusLength = CGFloat(size) * 0.58
    let stroke = CGFloat(size) * 0.105
    let center = CGPoint(x: bounds.midX, y: bounds.midY)
    context.setLineCap(.round)
    context.setLineJoin(.round)

    func dashedPlus(offsetY: CGFloat) {
        let half = plusLength / 2
        let dash = plusLength * 0.18
        let segments = [
            (-half, -half + dash),
            (-dash / 2, dash / 2),
            (half - dash, half),
        ]

        for (start, end) in segments {
            context.move(to: CGPoint(x: center.x + start, y: center.y + offsetY))
            context.addLine(to: CGPoint(x: center.x + end, y: center.y + offsetY))
            context.move(to: CGPoint(x: center.x, y: center.y + start + offsetY))
            context.addLine(to: CGPoint(x: center.x, y: center.y + end + offsetY))
        }
    }

    context.setStrokeColor(color(0xFFFFFF, alpha: 0.30))
    context.setLineWidth(stroke)
    context.beginPath()
    dashedPlus(offsetY: -CGFloat(size) * 0.01)
    context.strokePath()

    context.setStrokeColor(color(0xFFFFFF))
    context.setLineWidth(stroke)
    context.beginPath()
    dashedPlus(offsetY: 0)
    context.strokePath()

    let nsImage = NSImage(size: NSSize(width: size, height: size))
    nsImage.addRepresentation(bitmap)
    return nsImage
}

func writePNG(_ image: NSImage, to url: URL) throws {
    guard
        let tiff = image.tiffRepresentation,
        let bitmap = NSBitmapImageRep(data: tiff),
        let data = bitmap.representation(using: .png, properties: [:])
    else {
        throw NSError(domain: "IconGeneration", code: 1, userInfo: [NSLocalizedDescriptionKey: "Could not encode \(url.path)"])
    }
    try data.write(to: url)
}

let iconset = resources.appendingPathComponent("AppIcon.iconset", isDirectory: true)
try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

let iconFiles: [(String, Int)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
]

for (name, size) in iconFiles {
    try writePNG(appIcon(size: size, insetScale: 0.065, cornerScale: 0.19), to: iconset.appendingPathComponent(name))
}

func menuBarTemplateIcon(size: Int) -> NSImage {
    let bounds = CGRect(x: 0, y: 0, width: size, height: size)
    let bitmap = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: size,
        pixelsHigh: size,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    )!
    bitmap.size = NSSize(width: size, height: size)

    let context = NSGraphicsContext(bitmapImageRep: bitmap)!.cgContext
    context.setAllowsAntialiasing(true)
    context.setShouldAntialias(true)
    context.clear(bounds)

    let iconInset = CGFloat(size) * 0.16
    let rect = bounds.insetBy(dx: iconInset, dy: iconInset)
    let stroke = max(1.6, CGFloat(size) * 0.085)

    context.setStrokeColor(CGColor(gray: 0, alpha: 1))
    context.setLineWidth(stroke)
    context.setLineCap(.round)
    context.setLineJoin(.round)

    let borderPath = CGPath(
        roundedRect: rect,
        cornerWidth: CGFloat(size) * 0.13,
        cornerHeight: CGFloat(size) * 0.13,
        transform: nil
    )
    context.addPath(borderPath)
    context.strokePath()

    let plusLength = CGFloat(size) * 0.46
    let plusStroke = max(1.8, CGFloat(size) * 0.095)
    let center = CGPoint(x: bounds.midX, y: bounds.midY)
    let half = plusLength / 2
    let dash = plusLength * 0.17
    let segments = [
        (-half, -half + dash),
        (-dash / 2, dash / 2),
        (half - dash, half),
    ]

    context.setLineWidth(plusStroke)
    context.beginPath()
    for (start, end) in segments {
        context.move(to: CGPoint(x: center.x + start, y: center.y))
        context.addLine(to: CGPoint(x: center.x + end, y: center.y))
        context.move(to: CGPoint(x: center.x, y: center.y + start))
        context.addLine(to: CGPoint(x: center.x, y: center.y + end))
    }
    context.strokePath()

    let nsImage = NSImage(size: NSSize(width: size, height: size))
    nsImage.addRepresentation(bitmap)
    return nsImage
}

try writePNG(menuBarTemplateIcon(size: 36), to: resources.appendingPathComponent("MenuBarIconTemplate.png"))

let process = Process()
process.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
process.arguments = ["-c", "icns", iconset.path, "-o", resources.appendingPathComponent("AppIcon.icns").path]
try process.run()
process.waitUntilExit()
guard process.terminationStatus == 0 else {
    throw NSError(domain: "IconGeneration", code: Int(process.terminationStatus), userInfo: [NSLocalizedDescriptionKey: "iconutil failed"])
}

try? FileManager.default.removeItem(at: iconset)
print("Generated \(resources.appendingPathComponent("AppIcon.icns").path)")
print("Generated \(resources.appendingPathComponent("MenuBarIconTemplate.png").path)")
