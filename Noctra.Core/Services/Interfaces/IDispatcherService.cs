using System;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Service for marshalling actions to the UI thread (Avalonia Dispatcher)
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Executes the specified action on the UI thread synchronously
    /// </summary>
    void Invoke(Action action);

    /// <summary>
    /// Schedules the specified action to be executed on the UI thread asynchronously
    /// </summary>
    void BeginInvoke(Action action);

    /// <summary>
    /// Executes the specified asynchronous function on the UI thread
    /// </summary>
    Task InvokeAsync(Func<Task> function);

    /// <summary>
    /// Executes the specified function on the UI thread and returns the result
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> function);

    /// <summary>
    /// Executes the specified asynchronous function on the UI thread and returns the result
    /// </summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> function);
}

