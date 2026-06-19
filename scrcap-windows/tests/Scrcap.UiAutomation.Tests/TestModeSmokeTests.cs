using System.Diagnostics;
using System.IO;

namespace Scrcap.UiAutomation.Tests;

public sealed class TestModeSmokeTests
{
    [Fact]
    public void TestModeDumpsWindowsAndExitsWithoutResidentProcess()
    {
        var appDll = Path.Combine(AppContext.BaseDirectory, "Scrcap.Windows.UI.dll");
        Assert.True(File.Exists(appDll), $"Could not find app DLL at {appDll}.");

        using var temp = new TempDirectory();
        RunDump(appDll, temp.Path, "editor-light.png", "light", openPreferences: false);
        RunDump(appDll, temp.Path, "preferences-dark.png", "dark", openPreferences: true);
    }

    private static void RunDump(string appDll, string tempRoot, string fileName, string theme, bool openPreferences)
    {
        var settingsDir = Path.Combine(tempRoot, Path.GetFileNameWithoutExtension(fileName) + "-settings");
        Directory.CreateDirectory(settingsDir);
        var dumpPath = Path.Combine(tempRoot, fileName);

        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add(appDll);
        startInfo.ArgumentList.Add("--test-mode");
        if (openPreferences)
        {
            startInfo.ArgumentList.Add("--open-preferences");
        }

        startInfo.ArgumentList.Add("--dump-window-png");
        startInfo.ArgumentList.Add(dumpPath);
        startInfo.ArgumentList.Add("--test-app-theme");
        startInfo.ArgumentList.Add(theme);
        startInfo.ArgumentList.Add("--test-settings-dir");
        startInfo.ArgumentList.Add(settingsDir);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start scrcap test process.");
        var exited = process.WaitForExit(15_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(exited, $"scrcap test process did not exit. stdout: {stdout} stderr: {stderr}");
        Assert.Equal(0, process.ExitCode);
        Assert.True(File.Exists(dumpPath), $"Expected dump at {dumpPath}. stdout: {stdout} stderr: {stderr}");
        Assert.True(new FileInfo(dumpPath).Length > 1_000, $"Dump was unexpectedly small: {dumpPath}");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-ui-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }
}
