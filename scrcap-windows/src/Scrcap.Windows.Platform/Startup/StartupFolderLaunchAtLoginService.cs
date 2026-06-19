using System.Diagnostics;
using System.Text;

namespace Scrcap.Windows.Platform.Startup;

public sealed class StartupFolderLaunchAtLoginService : ILaunchAtLoginService
{
    private const string ScriptName = "scrcap.cmd";
    private readonly string startupDirectory;

    public StartupFolderLaunchAtLoginService()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.Startup))
    {
    }

    public StartupFolderLaunchAtLoginService(string startupDirectory)
    {
        this.startupDirectory = startupDirectory;
    }

    public bool IsEnabled() => File.Exists(ScriptPath);

    public bool SetEnabled(bool enabled, string? executablePath = null)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(ScriptPath))
                {
                    File.Delete(ScriptPath);
                }

                return true;
            }

            var target = executablePath ?? CurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
            {
                return false;
            }

            Directory.CreateDirectory(startupDirectory);
            File.WriteAllText(ScriptPath, ScriptFor(target), Encoding.ASCII);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string ScriptPath => Path.Combine(startupDirectory, ScriptName);

    private static string CurrentExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return processPath;
        }

        return Process.GetCurrentProcess().MainModule?.FileName
               ?? Path.Combine(AppContext.BaseDirectory, "Scrcap.Windows.UI.exe");
    }

    private static string ScriptFor(string executablePath) =>
        "@echo off\r\n"
        + "start \"\" \"" + executablePath.Replace("\"", "\"\"") + "\"\r\n";
}
