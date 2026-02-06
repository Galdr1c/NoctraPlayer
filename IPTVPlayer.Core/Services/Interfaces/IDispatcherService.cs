using System;

namespace IPTVPlayer.Services.Interfaces;

public interface IDispatcherService
{
    void Invoke(Action action);
    Task InvokeAsync(Func<Task> function);
}
