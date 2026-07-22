namespace Noctra.ViewModels;

internal sealed class SettingsAutoSaveCoordinator : IDisposable
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

                await _saveAsync();

                lock (_sync)
                {
                    _completedVersion = Math.Max(_completedVersion, version);
                    if (_activeVersion == version)
                    {
                        _activeVersion = 0;
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

    public void Dispose()
    {
        CancellationTokenSource? pendingDelay;
        long flushVersion;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pendingDelay = _delayCts;
            _delayCts = null;
            flushVersion = _requestedVersion > Math.Max(_completedVersion, _activeVersion)
                ? _requestedVersion
                : 0;
        }

        pendingDelay?.Cancel();
        pendingDelay?.Dispose();

        if (flushVersion > 0)
        {
            _ = FlushAfterDisposeAsync(flushVersion);
        }
    }

    private async Task FlushAfterDisposeAsync(long version)
    {
        await _saveGate.WaitAsync();
        try
        {
            await _saveAsync();
            lock (_sync)
            {
                _completedVersion = Math.Max(_completedVersion, version);
            }
        }
        finally
        {
            _saveGate.Release();
        }
    }
}
