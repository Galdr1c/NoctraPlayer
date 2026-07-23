using Microsoft.Extensions.DependencyInjection;

namespace Noctra.Services;

/// <summary>
/// Owns a service resolved from a child dependency-injection scope.
/// Disposing the lease releases the service and every other disposable created by that scope.
/// Supports both synchronous <see cref="IDisposable"/> and asynchronous
/// <see cref="IAsyncDisposable"/> disposal so that services implementing
/// only <c>IAsyncDisposable</c> (e.g. SettingsViewModel) can be disposed
/// without throwing from the DI container.
/// </summary>
public sealed class ScopedServiceLease<T> : IDisposable, IAsyncDisposable
    where T : notnull
{
    private IServiceScope? _scope;

    private ScopedServiceLease(IServiceScope scope, T service)
    {
        _scope = scope;
        Service = service;
    }

    public T Service { get; }

    public static ScopedServiceLease<T> Create(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var scope = serviceProvider.CreateScope();
        try
        {
            return new ScopedServiceLease<T>(
                scope,
                scope.ServiceProvider.GetRequiredService<T>());
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _scope, null)?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        var scope = Interlocked.Exchange(ref _scope, null);
        if (scope is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            scope?.Dispose();
        }
    }
}
