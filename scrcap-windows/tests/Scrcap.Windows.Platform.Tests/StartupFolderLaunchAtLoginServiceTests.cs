using Scrcap.Windows.Platform.Startup;

namespace Scrcap.Windows.Platform.Tests;

public sealed class StartupFolderLaunchAtLoginServiceTests
{
    [Fact]
    public void SetEnabledCreatesAndRemovesPerUserStartupScript()
    {
        using var temp = new TempDirectory();
        var exe = Path.Combine(temp.Path, "scrcap.exe");
        File.WriteAllText(exe, string.Empty);

        var service = new StartupFolderLaunchAtLoginService(temp.Path);

        Assert.True(service.SetEnabled(true, exe));
        Assert.True(service.IsEnabled());

        var script = Path.Combine(temp.Path, "scrcap.cmd");
        Assert.True(File.Exists(script));
        Assert.Contains(exe, File.ReadAllText(script), StringComparison.Ordinal);

        Assert.True(service.SetEnabled(false));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabledRefusesMissingExecutable()
    {
        using var temp = new TempDirectory();
        var service = new StartupFolderLaunchAtLoginService(temp.Path);

        Assert.False(service.SetEnabled(true, Path.Combine(temp.Path, "missing.exe")));
        Assert.False(service.IsEnabled());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scrcap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
