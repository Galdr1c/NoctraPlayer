using System.Globalization;
using System.Text.Json.Serialization;

namespace Noctra.Performance;

public sealed class SyntheticXtreamResponseFactory
{
    private readonly SyntheticXtreamPlan _plan;
    private readonly IReadOnlyList<SyntheticXtreamCategory> _categories;

    public SyntheticXtreamResponseFactory(SyntheticXtreamPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _categories = plan.GetCategories();
    }

    public object CreateAuthPayload()
        => new SyntheticAuthPayload(new SyntheticUserInfo("Active"));

    public IReadOnlyList<SyntheticCategoryResponse> CreateCategories(string kind)
    {
        var normalizedKind = NormalizeKind(kind);
        return _categories
            .Where(category => category.Type == normalizedKind)
            .Select(category => new SyntheticCategoryResponse(
                category.CategoryId,
                category.CategoryName))
            .ToList();
    }

    public IEnumerable<object> CreateItems(string kind, string categoryId)
    {
        var normalizedKind = NormalizeKind(kind);
        var category = _categories.FirstOrDefault(candidate =>
            candidate.Type == normalizedKind && candidate.CategoryId == categoryId);
        if (category is null)
        {
            return [];
        }

        var kindCategories = _categories
            .Where(candidate => candidate.Type == normalizedKind)
            .ToList();
        var categoryIndex = kindCategories.FindIndex(candidate =>
            candidate.CategoryId == categoryId);
        var itemCount = normalizedKind switch
        {
            "live" => _plan.LiveCount,
            "vod" => _plan.VodCount,
            "series" => _plan.SeriesCount,
            _ => 0
        };

        return EnumerateItems(
            normalizedKind,
            categoryId,
            categoryIndex,
            kindCategories.Count,
            itemCount);
    }

    private IEnumerable<object> EnumerateItems(
        string kind,
        string categoryId,
        int firstIndex,
        int stride,
        int itemCount)
    {
        for (var index = firstIndex; index < itemCount; index += stride)
        {
            var item = _plan.CreateItem(kind, index);
            var numericId = long.Parse(item.StreamId, CultureInfo.InvariantCulture);
            yield return kind switch
            {
                "live" => new SyntheticLiveResponse(
                    item.Name,
                    numericId,
                    null,
                    $"synthetic-live-{numericId}",
                    categoryId),
                "vod" => new SyntheticVodResponse(
                    item.Name,
                    numericId,
                    null,
                    null,
                    categoryId,
                    "mp4"),
                "series" => new SyntheticSeriesResponse(
                    numericId,
                    item.Name,
                    null,
                    categoryId),
                _ => throw new InvalidOperationException($"Unsupported Xtream kind: {kind}")
            };
        }
    }

    private static string NormalizeKind(string kind)
    {
        var normalized = kind?.Trim().ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(kind));
        return normalized is "live" or "vod" or "series"
            ? normalized
            : throw new ArgumentException($"Unsupported Xtream content kind: {kind}", nameof(kind));
    }
}

public sealed record SyntheticAuthPayload(
    [property: JsonPropertyName("user_info")] SyntheticUserInfo UserInfo);

public sealed record SyntheticUserInfo(
    [property: JsonPropertyName("status")] string Status);

public sealed record SyntheticCategoryResponse(
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("category_name")] string CategoryName);

public sealed record SyntheticLiveResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("stream_id")] long StreamId,
    [property: JsonPropertyName("stream_icon")] string? StreamIcon,
    [property: JsonPropertyName("epg_channel_id")] string? EpgChannelId,
    [property: JsonPropertyName("category_id")] string CategoryId);

public sealed record SyntheticVodResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("stream_id")] long StreamId,
    [property: JsonPropertyName("stream_icon")] string? StreamIcon,
    [property: JsonPropertyName("cover")] string? Cover,
    [property: JsonPropertyName("category_id")] string CategoryId,
    [property: JsonPropertyName("container_extension")] string ContainerExtension);

public sealed record SyntheticSeriesResponse(
    [property: JsonPropertyName("series_id")] long SeriesId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cover")] string? Cover,
    [property: JsonPropertyName("category_id")] string CategoryId);
