using Avalonia.Threading;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class AvaloniaDispatcherService : IDispatcherService
{
    public void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Invoke(action);
    }

    public void BeginInvoke(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeAsync(Func<Task> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await function();
            return;
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await function();
                tcs.SetResult(null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        await tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(function());
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                tcs.SetResult(function());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await function();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var result = await function();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return await tcs.Task;
    }
}
