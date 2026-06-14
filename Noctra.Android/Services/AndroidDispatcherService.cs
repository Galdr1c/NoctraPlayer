using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidDispatcherService : IDispatcherService
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

    public void BeginInvoke(Action action) => Dispatcher.UIThread.Post(action);

    public async Task InvokeAsync(Func<Task> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await function();
            return;
        }

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await function();
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        await completion.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return Task.FromResult(function());
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    public async Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await function();
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.SetResult(await function());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return await completion.Task;
    }
}
