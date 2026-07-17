namespace Noctra.Threading;

/// <summary>
/// Owns one active cancellation generation. Starting a new generation cancels
/// every operation linked to the previous token.
/// </summary>
public sealed class SupersedingCancellationScope : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _current = new();
    private int _generation;

    public CancellationTokenSource CreateLinkedTokenSource(
        CancellationToken cancellationToken,
        out int generation)
    {
        lock (_gate)
        {
            var current = _current ?? throw new ObjectDisposedException(
                nameof(SupersedingCancellationScope));
            generation = _generation;
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                current.Token);
        }
    }

    public int Supersede()
    {
        CancellationTokenSource previous;
        int generation;

        lock (_gate)
        {
            previous = _current ?? throw new ObjectDisposedException(
                nameof(SupersedingCancellationScope));
            _current = new CancellationTokenSource();
            generation = ++_generation;
        }

        previous.Cancel();
        previous.Dispose();
        return generation;
    }

    public int CaptureGeneration()
    {
        lock (_gate)
        {
            _ = _current ?? throw new ObjectDisposedException(
                nameof(SupersedingCancellationScope));
            return _generation;
        }
    }

    public bool TrySupersede(int expectedGeneration, out int generation)
    {
        CancellationTokenSource previous;

        lock (_gate)
        {
            previous = _current ?? throw new ObjectDisposedException(
                nameof(SupersedingCancellationScope));
            if (_generation != expectedGeneration)
            {
                generation = _generation;
                return false;
            }

            _current = new CancellationTokenSource();
            generation = ++_generation;
        }

        previous.Cancel();
        previous.Dispose();
        return true;
    }

    public bool IsCurrent(int generation)
    {
        lock (_gate)
        {
            return _current != null && generation == _generation;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? current;
        lock (_gate)
        {
            current = _current;
            _current = null;
        }

        if (current == null)
        {
            return;
        }

        current.Cancel();
        current.Dispose();
    }
}
