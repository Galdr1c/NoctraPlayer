using IPTVPlayer.Services.Interfaces;
using System.Windows;

namespace IPTVPlayer.Services;

public class WpfDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        Application.Current.Dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> function)
    {
        return Application.Current.Dispatcher.InvokeAsync(function).Task.Unwrap();
    }
}
