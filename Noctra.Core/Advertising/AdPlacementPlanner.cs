namespace Noctra.Core.Advertising;

/// <summary>
/// Maps "at least N real items between ads" to complete virtualized rows.
/// Ads are never inserted into a partially filled media row.
/// </summary>
public static class AdPlacementPlanner
{
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

        var rowsBetweenAds = checked(
            (policy.MinContentSpacing + columns - 1) / columns);
        var contentBetweenAds = checked(rowsBetweenAds * columns);

        var anchors = new int[policy.MaxSlots];
        for (var index = 0; index < anchors.Length; index++)
        {
            anchors[index] = checked(contentBetweenAds * (index + 1));
        }

        return anchors;
    }
}
