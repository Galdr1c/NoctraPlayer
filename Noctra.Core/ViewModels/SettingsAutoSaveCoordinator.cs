namespace Noctra.ViewModels;

internal sealed class SettingsAutoSaveCoordinator : IAsyncDisposable
{
    private readonly Func<Task> _saveAsync;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource? _delayCts;
    private long _requestedVersion;
    private long _activeVersion;
    private long _completedVersion;
    private bool _disposed;

    internal SettingsAutoSaveCoordinator(Func<Task> saveAsync)
    {
        _saveAsync = saveAsync ?? throw new ArgumentNullException(nameof(saveAsync));
    }

    internal void RequestSave(TimeSpan delay)
    {
        CancellationTokenSource previousDelay;
        CancellationTokenSource currentDelay;
        long version;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            version = ++_requestedVersion;
            previousDelay = _delayCts ?? new CancellationTokenSource();
            currentDelay = new CancellationTokenSource();
            _delayCts = currentDelay;
        }

        previousDelay.Cancel();
        previousDelay.Dispose();
        _ = SaveAfterDelayAsync(version, delay, currentDelay.Token);
    }

    private async Task SaveAfterDelayAsync(long version, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            await _saveGate.WaitAsync(cancellationToken);
            try
            {
                lock (_sync)
                {
                    if (_disposed || version != _requestedVersion)
                    {
                        return;
                    }

                    _activeVersion = version;
                }

                try
                {
                    await _saveAsync();

                    lock (_sync)
                    {
                        _completedVersion = Math.Max(_completedVersion, version);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsAutoSaveCoordinator] Save failed (v{version}): {ex.Message}");
                    // Do NOT advance _completedVersion — allow DisposeAsync to retry.
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_activeVersion == version)
                        {
                            _activeVersion = 0;
                        }
                    }
                }
            }
            finally
            {
                _saveGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this delayed request.
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? pendingDelay;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pendingDelay = _delayCts;
            _delayCts = null;
        }

        pendingDelay?.Cancel();
        pendingDelay?.Dispose();

        // Always wait behind the gate so any in-flight save completes and
        // scoped services are still alive when _saveAsync finishes.
        await _saveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            long flushVersion;
            lock (_sync)
            {
                flushVersion = _requestedVersion > _completedVersion
                    ? _requestedVersion
                    : 0;
            }

            if (flushVersion > 0)
            {
                try
                {
                    await _saveAsync().ConfigureAwait(false);
                    lock (_sync)
                    {
                        _completedVersion = Math.Max(_completedVersion, flushVersion);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SettingsAutoSaveCoordinator] Flush save failed (v{flushVersion}): {ex.Message}");
                }
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }

}
