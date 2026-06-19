namespace Scrcap.Windows.Platform.Startup;

public interface ILaunchAtLoginService
{
    bool IsEnabled();

    bool SetEnabled(bool enabled, string? executablePath = null);
}
