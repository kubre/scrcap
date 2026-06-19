using System.Windows;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.Rendering.Tests;

internal static class WpfTestHost
{
    private static readonly object Gate = new();

    public static void Run(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            lock (Gate)
            {
                try
                {
                    EnsureApplication();
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw exception;
        }
    }

    public static void ApplyTheme(Core.ThemeMode mode)
    {
        EnsureApplication();
        AppThemeService.Apply(Application.Current.Resources, mode);
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            _ = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
        }

        var app = Application.Current ?? throw new InvalidOperationException("WPF application was not created.");
        if (app.Resources.MergedDictionaries.Count == 0)
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Resources/ThemeTokens.xaml", UriKind.Relative),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Resources/IconGeometries.xaml", UriKind.Relative),
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Scrcap.Windows.UI;component/Chrome/ScrcapWindowChrome.xaml", UriKind.Relative),
            });
        }
    }
}
