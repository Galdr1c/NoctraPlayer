using Noctra.Models;

namespace Noctra.ViewModels;

/// <summary>
/// Time-based EPG data shared with presentation layers. This model deliberately
/// contains no viewport, pixel, focus, or styling information.
/// </summary>
public sealed class EpgGuideRow
{
    public Channel Channel { get; init; } = null!;

    public IReadOnlyList<EpgProgram> Programs { get; init; } = Array.Empty<EpgProgram>();

    public bool HasPrograms => Programs.Count > 0;
}

public enum EpgGuideLoadState
{
    Idle,
    Loading,
    Ready,
    Empty,
    Error
}

/// <summary>
/// Identifies the last successfully loaded guide so reopening the panel does not
/// discard and immediately reload the same large channel/program collection.
/// </summary>
public readonly record struct EpgGuideCacheSnapshot(
    DateTime LoadedAtUtc,
    string ChannelKey,
    DateTime WindowStart,
    DateTime WindowEnd)
{
    public bool CanReuse(
        DateTime nowUtc,
        TimeSpan maximumAge,
        string channelKey,
        DateTime windowStart,
        DateTime windowEnd)
    {
        var age = nowUtc - LoadedAtUtc;
        return age >= TimeSpan.Zero
               && age <= maximumAge
               && string.Equals(ChannelKey, channelKey, StringComparison.Ordinal)
               && WindowStart == windowStart
               && WindowEnd == windowEnd;
    }
}
