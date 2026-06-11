import AppKit

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
// Menu-bar resident: no Dock icon, no main window. (LSUIElement in the
// bundle's Info.plist covers the packaged app; this covers `swift run`.)
app.setActivationPolicy(.accessory)
app.run()
