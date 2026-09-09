namespace Noctra.UI.Layout;

public enum AdaptiveCardGridKind
{
    Live,
    Vod,
    Series,
    ContinueWatching
}

public readonly record struct AdaptiveCardGridMetrics(int Columns, double CardWidth)
{
    private const double CardGap = 16;

    public static AdaptiveCardGridMetrics Calculate(
        double availableWidth,
        AdaptiveCardGridKind kind)
    {
        var width = double.IsFinite(availableWidth) && availableWidth >= 2
            ? availableWidth
            : 2;
        var profile = kind is AdaptiveCardGridKind.Live or AdaptiveCardGridKind.ContinueWatching
            ? new GridProfile(220, 410, 4)
            : new GridProfile(150, 180, 6);

        var columns = Math.Max(
            1,
            (int)Math.Floor((width + CardGap) / (profile.MinimumWidth + CardGap)));
        columns = Math.Min(columns, profile.MaximumColumns);

        var cardWidth = Math.Floor((width - CardGap * (columns - 1)) / columns);
        cardWidth = Math.Clamp(cardWidth, 2, profile.MaximumWidth);
        cardWidth = Math.Max(2, Math.Floor(cardWidth / 2) * 2);
        return new(columns, cardWidth);
    }

    private readonly record struct GridProfile(
        double MinimumWidth,
        double MaximumWidth,
        int MaximumColumns);
}
