using System.Diagnostics;

namespace Scrcap.Core;

/// <summary>Stages complete screenshot bytes beside the destination before publication.</summary>
public static class ScreenshotFileWriter
{
    public static string WriteUnique(byte[] bytes, string directory, string filename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        if (Path.GetFileName(filename) != filename || filename is "." or "..")
        {
            throw new ArgumentException("Expected a filename, not a path.", nameof(filename));
        }
        return WriteStaged(bytes, Path.Combine(directory, filename), replace: false);
    }

    public static string WriteReplacing(byte[] bytes, string path) => WriteStaged(bytes, path, replace: true);

    private static string WriteStaged(byte[] bytes, string path, bool replace)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length == 0)
        {
            throw new ArgumentException("Screenshot payload is empty; no file was written.", nameof(bytes));
        }
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        var staged = Path.Combine(directory, $".scrcap-{Guid.NewGuid():N}.tmp");
        var ownsStagedFile = false;
        try
        {
            using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsStagedFile = true;
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            if (replace)
            {
                // Unlike WriteAllBytes, a failed encode/write has not truncated
                // the user's old file. Source and destination share a volume.
                File.Move(staged, path, overwrite: true);
                return path;
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var candidate = path;
            for (var suffix = 2; ; suffix = checked(suffix + 1))
            {
                try
                {
                    File.Move(staged, candidate, overwrite: false);
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    // No existence-check/write race: Move itself refuses replacement.
                    candidate = Path.Combine(directory, $"{stem}-{suffix}{extension}");
                }
            }
        }
        finally
        {
            if (ownsStagedFile)
            {
                try { File.Delete(staged); }
                catch (IOException error) { Trace.TraceWarning($"Cannot remove screenshot staging file {staged}: {error}"); }
                catch (UnauthorizedAccessException error) { Trace.TraceWarning($"Cannot remove screenshot staging file {staged}: {error}"); }
            }
        }
    }
}
