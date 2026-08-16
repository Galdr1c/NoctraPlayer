namespace Noctra.Core.Advertising;

/// <summary>
/// Maps "at least N real items between ads" to complete virtualized rows.
/// Ads are never inserted into a partially filled media row.
///
/// Defense-in-depth: even if a caller passes unbounded options (e.g. a
/// misconfigured remote config), the planner caps the anchor allocation and
/// uses 64-bit arithmetic, so it can never throw on overflow or allocate an
/// oversized array.
/// </summary>
public static class AdPlacementPlanner
{
    private const int MaxAnchorCount = 8;

    public static bool IsEligibleContentCount(
        int realContentCount,
        NativeAdPlacementOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return policy.Enabled &&
               policy.MaxSlots > 0 &&
               policy.MinContentSpacing > 0 &&
               realContentCount >= policy.MinContentSpacing;
    }

    public static IReadOnlyList<int> GetContentAnchors(
        int columns,
        NativeAdPlacementOptions policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.Enabled ||
            columns <= 0 ||
            policy.MinContentSpacing <= 0 ||
            policy.MaxSlots <= 0)
        {
            return Array.Empty<int>();
        }

        var slotCount = Math.Min(policy.MaxSlots, MaxAnchorCount);
        var rowsBetweenAds = ((long)policy.MinContentSpacing + columns - 1) / columns;
        var contentBetweenAds = Math.Min(rowsBetweenAds * columns, int.MaxValue);

        var anchors = new int[slotCount];
        for (var index = 0; index < anchors.Length; index++)
        {
            anchors[index] = (int)Math.Min(
                contentBetweenAds * (index + 1L),
                int.MaxValue);
        }

        return anchors;
    }

    /// <summary>
    /// Number of ad slots that are actually reachable with the current content
    /// count. An anchor becomes reachable only when the real content count has
    /// reached that anchor, so preloading never requests ads for rows the UI
    /// cannot render yet (e.g. 1 movie must not prime 2 native ads).
    /// </summary>
    public static int GetReachableSlotCount(
        IReadOnlyList<int> anchors,
        int realContentCount)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        if (realContentCount <= 0 || anchors.Count == 0)
        {
            return 0;
        }

        return anchors.Count(anchor => anchor > 0 && anchor <= realContentCount);
    }
}
