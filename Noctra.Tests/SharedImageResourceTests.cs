using Noctra.Core.Collections;

namespace Noctra.Tests;

public sealed class SharedImageResourceTests
{
    [Fact]
    public void ConsumerLease_KeepsValueAliveAfterProducerOwnerReleases()
    {
        var releases = 0;
        var baselineBytes = SharedImageResource<string>.CurrentOwnedBytes;
        var resource = new SharedImageResource<string>(
            "bitmap",
            sizeBytes: 4096,
            _ => Interlocked.Increment(ref releases));
        var consumer = resource.AcquireConsumerLease();

        resource.Dispose();

        Assert.Equal("bitmap", consumer.Value);
        Assert.Equal(0, releases);
        Assert.Equal(baselineBytes + 4096, SharedImageResource<string>.CurrentOwnedBytes);

        consumer.Dispose();

        Assert.Equal(1, releases);
        Assert.Equal(baselineBytes, SharedImageResource<string>.CurrentOwnedBytes);
    }

    [Fact]
    public void CacheAndConsumerLeases_ReleaseValueOnlyAfterBothEnd()
    {
        var releases = 0;
        var resource = new SharedImageResource<string>(
            "bitmap",
            sizeBytes: 128,
            _ => releases++);
        var cache = resource.AcquireCacheLease();
        var consumer = resource.AcquireConsumerLease();

        resource.Dispose();
        cache.Dispose();

        Assert.Equal(0, releases);
        Assert.Equal("bitmap", consumer.Value);

        consumer.Dispose();

        Assert.Equal(1, releases);
    }

    [Fact]
    public void OwnersAndLeases_AreIdempotentAndCannotBeAcquiredAfterFinalRelease()
    {
        var releases = 0;
        var resource = new SharedImageResource<string>(
            "bitmap",
            sizeBytes: 1,
            _ => releases++);
        var lease = resource.AcquireConsumerLease();

        lease.Dispose();
        lease.Dispose();
        resource.Dispose();
        resource.Dispose();

        Assert.Equal(1, releases);
        Assert.Throws<ObjectDisposedException>(() => resource.AcquireConsumerLease());
        Assert.Throws<ObjectDisposedException>(() => resource.AcquireCacheLease());
    }

    [Fact]
    public void SuccessfulCachePublication_TransfersProducerOwnershipUntilCacheEvicts()
    {
        var releases = 0;
        var resource = new SharedImageResource<string>("bitmap", 1, _ => releases++);

        Assert.True(resource.TryPublishToCache(() => true));
        resource.ReleaseProducerIfNotPublished();

        Assert.Equal(0, releases);

        resource.Dispose();
        Assert.Equal(1, releases);
    }

    [Fact]
    public void RejectedCachePublication_ReleasesProducerOwnership()
    {
        var releases = 0;
        var resource = new SharedImageResource<string>("bitmap", 1, _ => releases++);

        Assert.False(resource.TryPublishToCache(() => false));
        resource.ReleaseProducerIfNotPublished();

        Assert.Equal(1, releases);
    }
}
