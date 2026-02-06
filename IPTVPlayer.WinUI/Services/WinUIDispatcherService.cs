using IPTVPlayer.Services.Interfaces;
using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace IPTVPlayer.WinUI.Services;

public class WinUIDispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUIDispatcherService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public void Invoke(Action action)
    {
        if (_dispatcherQueue == null) return;
        
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    public Task InvokeAsync(Func<Task> function)
    {
        if (_dispatcherQueue == null) return Task.CompletedTask;
        
        if (_dispatcherQueue.HasThreadAccess)
        {
            return function();
        }
        else
        {
            var tcs = new TaskCompletionSource();
            _dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await function();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
