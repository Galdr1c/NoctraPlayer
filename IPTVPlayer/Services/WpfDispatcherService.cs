using IPTVPlayer.Services.Interfaces;
using System.Windows;

namespace IPTVPlayer.Services;

public class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        Application.Current.Dispatcher.Invoke(action);
    }

    public void BeginInvoke(Action action)
    {
        Application.Current.Dispatcher.BeginInvoke(action);
    }

    public Task InvokeAsync(Func<Task> function)
    {
        return Application.Current.Dispatcher.InvokeAsync(function).Task.Unwrap();
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        return Application.Current.Dispatcher.InvokeAsync(function).Task;
    }

    public Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        return Application.Current.Dispatcher.InvokeAsync(function).Task.Unwrap();
    }
}
