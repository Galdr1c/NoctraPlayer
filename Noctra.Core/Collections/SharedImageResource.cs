using Noctra.Diagnostics;

namespace Noctra.Core.Collections;

/// <summary>
/// Owns a native-backed image value until its producer, cache, and consumer
/// references have all been released.
/// </summary>
public sealed class SharedImageResource<TValue> : IDisposable
{
    private static long _currentOwnedBytes;
    private static long _peakOwnedBytes;
    private static long _liveResourceCount;
    private static long _activeConsumerLeaseCount;

    private readonly object _gate = new();
    private readonly TValue _value;
    private readonly long _sizeBytes;
    private readonly Action<TValue> _releaseValue;
    private int _referenceCount = 1;
    private bool _producerReleased;
    private bool _valueReleased;
    private int _cachePublicationState;

    public SharedImageResource(
        TValue value,
        long sizeBytes,
        Action<TValue> releaseValue)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);
        ArgumentNullException.ThrowIfNull(releaseValue);

        _value = value;
        _sizeBytes = sizeBytes;
        _releaseValue = releaseValue;

        var currentBytes = Interlocked.Add(ref _currentOwnedBytes, sizeBytes);
        Interlocked.Increment(ref _liveResourceCount);
        UpdatePeak(currentBytes);
        ReportOwnership();
    }

    public static long CurrentOwnedBytes => Interlocked.Read(ref _currentOwnedBytes);
    public static long PeakOwnedBytes => Interlocked.Read(ref _peakOwnedBytes);
    public static long LiveResourceCount => Interlocked.Read(ref _liveResourceCount);
    public static long ActiveConsumerLeaseCount => Interlocked.Read(ref _activeConsumerLeaseCount);

    public long SizeBytes => _sizeBytes;

    public SharedImageLease<TValue> AcquireConsumerLease()
        => AcquireLease(isConsumer: true);

    public SharedImageLease<TValue> AcquireCacheLease()
        => AcquireLease(isConsumer: false);

    public bool TryPublishToCache(Func<bool> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (Interlocked.CompareExchange(ref _cachePublicationState, 1, 0) != 0)
        {
            return false;
        }

        try
        {
            var published = publish();
            Volatile.Write(ref _cachePublicationState, published ? 2 : 3);
            return published;
        }
        catch
        {
            Volatile.Write(ref _cachePublicationState, 0);
            throw;
        }
    }

    public void ReleaseProducerIfNotPublished()
    {
        if (Volatile.Read(ref _cachePublicationState) != 2)
        {
            Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_producerReleased)
            {
                return;
            }

            _producerReleased = true;
        }

        ReleaseReference(isConsumer: false);
    }

    internal TValue GetValue()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_valueReleased, this);
            return _value;
        }
    }

    internal void ReleaseLease(bool isConsumer)
        => ReleaseReference(isConsumer);

    private SharedImageLease<TValue> AcquireLease(bool isConsumer)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_valueReleased, this);
            checked
            {
                _referenceCount++;
            }
        }

        if (isConsumer)
        {
            Interlocked.Increment(ref _activeConsumerLeaseCount);
            ReportConsumerLeases();
        }

        return new SharedImageLease<TValue>(this, isConsumer);
    }

    private void ReleaseReference(bool isConsumer)
    {
        var releaseValue = false;
        lock (_gate)
        {
            if (_referenceCount <= 0)
            {
                return;
            }

            _referenceCount--;
            if (_referenceCount == 0 && !_valueReleased)
            {
                _valueReleased = true;
                releaseValue = true;
            }
        }

        if (isConsumer)
        {
            Interlocked.Decrement(ref _activeConsumerLeaseCount);
            ReportConsumerLeases();
        }

        if (!releaseValue)
        {
            return;
        }

        try
        {
            _releaseValue(_value);
        }
        finally
        {
            Interlocked.Add(ref _currentOwnedBytes, -_sizeBytes);
            Interlocked.Decrement(ref _liveResourceCount);
            ReportOwnership();
        }
    }

    private static void UpdatePeak(long candidate)
    {
        while (true)
        {
            var currentPeak = Interlocked.Read(ref _peakOwnedBytes);
            if (candidate <= currentPeak ||
                Interlocked.CompareExchange(ref _peakOwnedBytes, candidate, currentPeak) == currentPeak)
            {
                return;
            }
        }
    }

    private static void ReportOwnership()
    {
        PerformanceTrace.Mark("ImageOwnedNativeBytes", Interlocked.Read(ref _currentOwnedBytes));
        PerformanceTrace.Mark("ImageOwnedNativePeakBytes", Interlocked.Read(ref _peakOwnedBytes));
        PerformanceTrace.Mark("ImageOwnedResourceCount", Interlocked.Read(ref _liveResourceCount));
    }

    private static void ReportConsumerLeases()
        => PerformanceTrace.Mark(
            "ImageActiveConsumerLeases",
            Interlocked.Read(ref _activeConsumerLeaseCount));
}

public sealed class SharedImageLease<TValue> : IDisposable
{
    private SharedImageResource<TValue>? _owner;
    private readonly bool _isConsumer;

    internal SharedImageLease(SharedImageResource<TValue> owner, bool isConsumer)
    {
        _owner = owner;
        _isConsumer = isConsumer;
    }

    public TValue Value
    {
        get
        {
            var owner = Volatile.Read(ref _owner);
            ObjectDisposedException.ThrowIf(owner is null, this);
            return owner.GetValue();
        }
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseLease(_isConsumer);
    }
}
