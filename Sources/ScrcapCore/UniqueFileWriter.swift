import Foundation
#if canImport(Darwin)
import Darwin
#endif

/// Publishes a complete file under a free name without replacing another save.
/// Foundation does not allow `.atomic` and `.withoutOverwriting` together.
public enum UniqueFileWriter {
    public static func write(_ data: Data, directory: URL, filename: String) throws -> URL {
        let manager = FileManager.default
        let stagingDirectory = try manager.url(
            for: .itemReplacementDirectory,
            in: .userDomainMask,
            appropriateFor: directory,
            create: true
        )
        defer {
            do {
                try manager.removeItem(at: stagingDirectory)
            } catch {
                NSLog("scrcap: could not remove save staging directory %@: %@", stagingDirectory.path, error.localizedDescription)
            }
        }
        let staged = stagingDirectory.appendingPathComponent("capture")
        try data.write(to: staged, options: .withoutOverwriting)

        var collisions = Set<String>()
        while true {
            let name = FilenameGenerator.availableFilename(filename) { candidate in
                collisions.contains(candidate) || manager.fileExists(atPath: directory.appendingPathComponent(candidate).path)
            }
            let destination = directory.appendingPathComponent(name)
            do {
                try publish(staged, to: destination)
                return destination
            } catch CocoaError.fileWriteFileExists {
                collisions.insert(name)
            }
        }
    }

    private static func publish(_ staged: URL, to destination: URL) throws {
        #if canImport(Darwin)
        // FileManager.moveItem performs a separate existence check: concurrent
        // moves can still overwrite. RENAME_EXCL checks and renames atomically,
        // and unlike a hard link it also works on volumes without link support.
        let errorCode = staged.withUnsafeFileSystemRepresentation { source in
            destination.withUnsafeFileSystemRepresentation { target -> Int32 in
                guard let source, let target else { return EINVAL }
                return renamex_np(source, target, UInt32(RENAME_EXCL)) == 0 ? 0 : errno
            }
        }
        guard errorCode != 0 else { return }
        let underlying = NSError(domain: NSPOSIXErrorDomain, code: Int(errorCode))
        if errorCode == EEXIST {
            throw CocoaError(.fileWriteFileExists, userInfo: [
                NSFilePathErrorKey: destination.path,
                NSUnderlyingErrorKey: underlying,
            ])
        }
        throw underlying
        #else
        // Portable-core tests use the same exclusive publication contract on
        // Linux. Leave the staging link for the enclosing deferred cleanup.
        try FileManager.default.linkItem(at: staged, to: destination)
        #endif
    }
}
