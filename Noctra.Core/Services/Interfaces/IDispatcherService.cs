using System;

namespace Noctra.Services.Interfaces;

public interface IDispatcherService
{
    void Invoke(Action action);
    void BeginInvoke(Action action);
    Task InvokeAsync(Func<Task> function);
    Task<T> InvokeAsync<T>(Func<T> function);
    Task<T> InvokeAsync<T>(Func<Task<T>> function);
}

