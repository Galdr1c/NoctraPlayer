namespace Noctra.Performance;

public sealed class SyntheticXtreamPlan
{
    private const int LiveCategoryPercent = 40;
    private const int VodCategoryPercent = 30;

    public SyntheticXtreamPlan(int totalCount, int categoryCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(categoryCount, 3);

        TotalCount = totalCount;
        CategoryCount = categoryCount;
        LiveCount = totalCount * 40 / 100;
        VodCount = totalCount * 30 / 100;
        SeriesCount = totalCount - LiveCount - VodCount;
    }

    public int TotalCount { get; }
    public int CategoryCount { get; }
    public int LiveCount { get; }
    public int VodCount { get; }
    public int SeriesCount { get; }

    public IReadOnlyList<SyntheticXtreamCategory> GetCategories()
    {
        var liveCategories = CategoryCount * LiveCategoryPercent / 100;
        var vodCategories = CategoryCount * VodCategoryPercent / 100;
        var seriesCategories = CategoryCount - liveCategories - vodCategories;
        var categories = new List<SyntheticXtreamCategory>(CategoryCount);

        AddCategories(categories, "live", liveCategories, 0);
        AddCategories(categories, "vod", vodCategories, liveCategories);
        AddCategories(categories, "series", seriesCategories, liveCategories + vodCategories);
        return categories;
    }

    public SyntheticXtreamItem CreateItem(string kind, int index)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(kind));
        var (itemCount, categoryOffset, kindCategoryCount, streamPrefix) = normalizedKind switch
        {
            "live" => (LiveCount, 0, CategoryCount * LiveCategoryPercent / 100, "live"),
            "vod" => (VodCount, CategoryCount * LiveCategoryPercent / 100,
                CategoryCount * VodCategoryPercent / 100, "movie"),
            "series" => (SeriesCount,
                CategoryCount * (LiveCategoryPercent + VodCategoryPercent) / 100,
                CategoryCount - CategoryCount * (LiveCategoryPercent + VodCategoryPercent) / 100,
                "series"),
            _ => throw new ArgumentException($"Unsupported Xtream content kind: {kind}", nameof(kind))
        };

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= itemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var categoryIndex = categoryOffset + index % kindCategoryCount;
        var stableId = categoryOffset * 1_000_000 + index + 1;
        return new SyntheticXtreamItem(
            normalizedKind,
            index,
            (categoryIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"Synthetic {normalizedKind} {index + 1:D7}",
            stableId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"http://127.0.0.1:18765/{streamPrefix}/benchmark/{stableId}");
    }

    private static void AddCategories(
        ICollection<SyntheticXtreamCategory> destination,
        string kind,
        int count,
        int offset)
    {
        for (var index = 0; index < count; index++)
        {
            var id = offset + index + 1;
            destination.Add(new SyntheticXtreamCategory(
                id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Synthetic {kind} {index + 1:D3}",
                kind));
        }
    }
}

public sealed record SyntheticXtreamCategory(
    string CategoryId,
    string CategoryName,
    string Type);

public sealed record SyntheticXtreamItem(
    string Kind,
    int Index,
    string CategoryId,
    string Name,
    string StreamId,
    string StreamUrl);
