using Noctra.Services.Interfaces;
using System.Windows;
using System.Windows.Threading;

namespace Noctra.Services;

public class WpfDispatcherService : IDispatcherService
{
    private static Dispatcher? TryGetDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return null;
        }

        return dispatcher;
    }

    public void Invoke(Action action)
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.Invoke(action);
    }

    public void BeginInvoke(Action action)
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public Task InvokeAsync(Func<Task> function)
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(function).Task.Unwrap();
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return Task.FromResult(default(T)!);
        }

        return dispatcher.InvokeAsync(function).Task;
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return Task.FromResult(default(T)!);
        }

        return dispatcher.InvokeAsync(function).Task.Unwrap();
    }
}
