using System.Windows;
using System.Windows.Threading;
using Scrcap.Windows.UI.Resources;

namespace Scrcap.Rendering.Tests;

internal static class WpfTestHost
{
    private static readonly object Gate = new();
    private static readonly Lazy<Dispatcher> Host = new(CreateDispatcher);

    public static void Run(Action action)
    {
        // Application and its resources belong to one living dispatcher. A new
        // STA per test leaves Application.Current attached to a terminated thread.
        lock (Gate)
        {
            Host.Value.Invoke(() =>
            {
                EnsureApplication();
                action();
            });
        }
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            ready.SetResult(dispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "scrcap rendering tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
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
        }
    }
}
