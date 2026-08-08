using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class EpgGuideCacheTests
{
    [Fact]
    public void CanReuse_RequiresFreshMatchingChannelsAndWindow()
    {
        var loadedAt = new DateTime(2026, 8, 8, 17, 0, 0, DateTimeKind.Utc);
        var windowStart = new DateTime(2026, 8, 8, 17, 30, 0, DateTimeKind.Local);
        var windowEnd = windowStart.AddHours(8);
        var cache = new EpgGuideCacheSnapshot(
            loadedAt,
            "10|20",
            windowStart,
            windowEnd);

        Assert.True(cache.CanReuse(
            loadedAt.AddMinutes(14),
            TimeSpan.FromMinutes(15),
            "10|20",
            windowStart,
            windowEnd));

        Assert.False(cache.CanReuse(
            loadedAt.AddMinutes(16),
            TimeSpan.FromMinutes(15),
            "10|20",
            windowStart,
            windowEnd));

        Assert.False(cache.CanReuse(
            loadedAt.AddMinutes(5),
            TimeSpan.FromMinutes(15),
            "10|30",
            windowStart,
            windowEnd));
    }
}
