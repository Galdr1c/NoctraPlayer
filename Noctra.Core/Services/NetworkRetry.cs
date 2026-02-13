using System.Net.Http;

namespace Noctra.Services;

public static class NetworkRetry
{
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, int maxAttempts = 3, CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;
                if (attempt == maxAttempts)
                {
                    break;
                }

                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new HttpRequestException("Network operation failed.");
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException ||
               ex is TaskCanceledException ||
               ex is TimeoutException;
    }
}

