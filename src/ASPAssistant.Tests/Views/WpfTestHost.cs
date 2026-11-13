using System.Windows;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Tests.Views;

// WPF permits only one Application per test process. All cases use this STA dispatcher.
public sealed class WpfTestHost : IAsyncLifetime
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread _thread;
    private string? _timedOutScenario;

    public WpfTestHost()
    {
        _thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                    });
                _ready.SetResult(application.Dispatcher);
                application.Run();
                _stopped.SetResult();
            }
            catch (Exception error)
            {
                _ready.TrySetException(error);
                _stopped.TrySetException(error);
            }
        }) { IsBackground = true, Name = "WPF test dispatcher" };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public async Task InitializeAsync()
    {
        _thread.Start();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public async Task RunAsync(Action test, string name)
    {
        if (_timedOutScenario is not null)
            throw new InvalidOperationException($"WPF dispatcher is blocked by timed-out scenario {_timedOutScenario}.");
        var dispatcher = await _ready.Task;
        var operation = dispatcher.InvokeAsync(() =>
        {
            // Existing synchronous helpers load data before pumping UI events.
            // Keep their original null context; helpers needing UI continuations install their own.
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            ThemeManager.Apply(GameShopTheme.Terminal, true);
            try { test(); }
            finally
            {
                foreach (var window in Application.Current.Windows.Cast<Window>().ToArray()) window.Close();
                ThemeManager.Apply(GameShopTheme.Terminal, true);
                SynchronizationContext.SetSynchronizationContext(context);
            }
        });
        try { await operation.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
        catch (TimeoutException error) when (!operation.Task.IsCompleted)
        {
            _timedOutScenario = name;
            operation.Abort();
            throw new TimeoutException($"WPF scenario {name} did not finish within 60 seconds.", error);
        }
    }

    public async Task DisposeAsync()
    {
        if (!_ready.Task.IsCompletedSuccessfully) return;
        var dispatcher = await _ready.Task;
        await dispatcher.InvokeAsync(() => Application.Current.Shutdown()).Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
