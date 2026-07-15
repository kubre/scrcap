namespace Scrcap.Windows.Platform.Startup;

public sealed record LaunchAtLoginResult(bool Succeeded, string? ErrorMessage = null)
{
    public static LaunchAtLoginResult Success { get; } = new(true);

    public static LaunchAtLoginResult Failure(string message) => new(false, message);
}

public interface ILaunchAtLoginService
{
    bool IsEnabled();

    LaunchAtLoginResult SetEnabled(bool enabled, string? executablePath = null);
}
