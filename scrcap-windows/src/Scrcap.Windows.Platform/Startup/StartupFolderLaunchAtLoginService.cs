using System.Diagnostics;
using System.Text;

namespace Scrcap.Windows.Platform.Startup;

/// <summary>
/// Registers the unpackaged application through the current user's Startup folder.
/// This works for framework-dependent apphosts and single-file published builds
/// without requiring elevation or package identity.
/// </summary>
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

    public LaunchAtLoginResult SetEnabled(bool enabled, string? executablePath = null)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(ScriptPath))
                {
                    File.Delete(ScriptPath);
                }

                return LaunchAtLoginResult.Success;
            }

            var command = executablePath is { Length: > 0 }
                ? LaunchCommand.ForExecutable(executablePath)
                : CurrentLaunchCommand();
            if (!File.Exists(command.ExecutablePath))
            {
                return LaunchAtLoginResult.Failure($"The scrcap executable was not found at '{command.ExecutablePath}'.");
            }

            Directory.CreateDirectory(startupDirectory);
            var tempPath = ScriptPath + ".tmp";
            File.WriteAllText(tempPath, ScriptFor(command), Encoding.ASCII);
            File.Move(tempPath, ScriptPath, overwrite: true);
            return LaunchAtLoginResult.Success;
        }
        catch (Exception ex)
        {
            return LaunchAtLoginResult.Failure(ex.Message);
        }
    }

    private string ScriptPath => Path.Combine(startupDirectory, ScriptName);

    private static LaunchCommand CurrentLaunchCommand()
    {
        var processPath = Environment.ProcessPath
                          ?? Process.GetCurrentProcess().MainModule?.FileName
                          ?? Path.Combine(AppContext.BaseDirectory, "Scrcap.Windows.UI.exe");
        var processName = Path.GetFileNameWithoutExtension(processPath);
        var firstArgument = Environment.GetCommandLineArgs().FirstOrDefault();

        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase)
            && firstArgument is { Length: > 0 }
            && Path.GetExtension(firstArgument).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            && File.Exists(firstArgument))
        {
            return new LaunchCommand(processPath, [Path.GetFullPath(firstArgument), "--launch-at-login"]);
        }

        return new LaunchCommand(processPath, ["--launch-at-login"]);
    }

    private static string ScriptFor(LaunchCommand command) =>
        "@echo off\r\n"
        + "start \"\" "
        + QuoteForBatch(command.ExecutablePath)
        + (command.Arguments.Count == 0 ? string.Empty : " " + string.Join(" ", command.Arguments.Select(QuoteForBatch)))
        + "\r\n";

    private static string QuoteForBatch(string value) =>
        "\"" + value.Replace("%", "%%", StringComparison.Ordinal).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed record LaunchCommand(string ExecutablePath, IReadOnlyList<string> Arguments)
    {
        public static LaunchCommand ForExecutable(string path) => new(Path.GetFullPath(path), ["--launch-at-login"]);
    }
}
