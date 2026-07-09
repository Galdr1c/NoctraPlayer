using System.Net;
using System.Text.Json;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class StalkerPortalServiceBatchTests
{
    [Fact]
    public async Task GetChannelsProgressiveBatchedAsync_EmitsPagesWithoutMaterializingCategory()
    {
        using var client = new HttpClient(new StalkerBatchHandler());
        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(service => service.GetString(It.IsAny<string>()))
            .Returns<string>(key => key);
        var service = new StalkerPortalService(client, localization.Object);
        var batches = new List<(int Count, bool Completed)>();

        await service.GetChannelsProgressiveBatchedAsync(
            $"http://stalker-batch-{Guid.NewGuid():N}.test",
            "00:1A:79:00:00:01",
            includeVod: false,
            onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
            onCategoryBatchLoaded: (channels, _, completed) =>
            {
                batches.Add((channels.Count, completed));
                return Task.CompletedTask;
            });

        Assert.Equal(new[] { 500, 500, 201 }, batches.Select(batch => batch.Count));
        Assert.Equal(new[] { false, false, true }, batches.Select(batch => batch.Completed));
    }

    private sealed class StalkerBatchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = ParseQuery(request.RequestUri?.Query);
            var action = query.GetValueOrDefault("action");
            object payload = action switch
            {
                "handshake" => new { js = new { token = "test-token" } },
                "get_profile" => new { js = new { id = "1" } },
                "get_genres" => new
                {
                    js = new[]
                    {
                        new { id = "10", title = "Large category", count = 1201 }
                    }
                },
                "get_ordered_list" => CreatePage(query),
                _ => new { js = Array.Empty<object>() }
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload))
            });
        }

        private static object CreatePage(IReadOnlyDictionary<string, string> query)
        {
            var page = int.TryParse(query.GetValueOrDefault("p"), out var parsedPage)
                ? parsedPage
                : 1;
            var start = (page - 1) * 500 + 1;
            var count = page < 3 ? 500 : 201;
            var data = Enumerable.Range(start, count)
                .Select(index => new
                {
                    id = index.ToString(),
                    name = $"Channel {index}",
                    cmd = $"ffmpeg http://stream.test/{index}",
                    tv_genre_id = "10"
                })
                .ToArray();

            return new
            {
                js = new
                {
                    data,
                    total_items = 1201,
                    max_page_items = 500
                }
            };
        }

        private static Dictionary<string, string> ParseQuery(string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(
                    part => Uri.UnescapeDataString(part[0]),
                    part => part.Length > 1 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }
    }
}
