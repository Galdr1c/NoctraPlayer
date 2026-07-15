using Moq;
using Moq.Protected;
using Noctra.Models;
using Noctra.Diagnostics;
using Noctra.Services;
using Noctra.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Noctra.Tests
{
    public class XtreamCodesServiceIntegrationTests
    {
        private readonly Mock<HttpMessageHandler> _handlerMock;
        private readonly HttpClient _httpClient;
        private readonly Mock<ILocalizationService> _localizationServiceMock;
        private readonly XtreamCodesService _service;

        public XtreamCodesServiceIntegrationTests()
        {
            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _httpClient = new HttpClient(_handlerMock.Object);
            _localizationServiceMock = new Mock<ILocalizationService>();
            
            // Setup default localization behavior
            _localizationServiceMock.Setup(l => l.GetString(It.IsAny<string>())).Returns<string>(k => k);

            _service = new XtreamCodesService(_httpClient, _localizationServiceMock.Object);
        }

        [Fact]
        public async Task AuthenticateAsync_WithValidCredentials_ReturnsTrue()
        {
            // Arrange
            SetupMockByAction("http://auth.com", null, new { user_info = new { status = "Active" } });

            // Act
            var result = await _service.AuthenticateAsync("http://auth.com", "user", "pass");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task GetChannelsAsync_MapsLiveStreamsCorrectly()
        {
            // Arrange
            string baseUrl = "http://live.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } }); // Auth
            SetupMockByAction(baseUrl, "get_live_categories", new[] { new { category_id = "1", category_name = "Sports" } });
            SetupMockByAction(baseUrl, "get_vod_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_series_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_live_streams", new[] { new { name = "ESPN", stream_id = 100, category_id = "1", epg_channel_id = "espn.epg" } });
            SetupMockByAction(baseUrl, "get_vod_streams", new List<object>());
            SetupMockByAction(baseUrl, "get_series", new List<object>());

            // Act
            var channels = await _service.GetChannelsAsync(baseUrl, "user", "pass", includeSeriesEpisodes: false);

            // Assert
            var channel = channels.First(c => c.Type == ChannelType.Live);
            Assert.Equal("ESPN", channel.Name);
            Assert.Equal("Sports", channel.GroupTitle);
        }

        [Fact]
        public async Task GetChannelsAsync_MapsVodStreamsCorrectly()
        {
            // Arrange
            string baseUrl = "http://vod.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } }); // Auth
            SetupMockByAction(baseUrl, "get_live_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_vod_categories", new[] { new { category_id = "2", category_name = "Movies" } });
            SetupMockByAction(baseUrl, "get_series_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_live_streams", new List<object>());
            SetupMockByAction(baseUrl, "get_vod_streams", new[] { new { name = "Inception", stream_id = 200, category_id = "2", year = "2010", rating = "8.8" } });
            SetupMockByAction(baseUrl, "get_series", new List<object>());

            // Act
            var channels = await _service.GetChannelsAsync(baseUrl, "user2", "pass2", includeSeriesEpisodes: false);

            // Assert
            var channel = channels.First(c => c.Type == ChannelType.VOD);
            Assert.Equal("Inception", channel.Name);
            Assert.Equal(2010, channel.ReleaseYear);
        }

        [Fact]
        public async Task GetChannelsAsync_MapsSeriesAndEpisodesCorrectly()
        {
            // Arrange
            string baseUrl = "http://series.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } }); // Auth
            SetupMockByAction(baseUrl, "get_live_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_vod_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_series_categories", new[] { new { category_id = "3", category_name = "Action" } });
            SetupMockByAction(baseUrl, "get_live_streams", new List<object>());
            SetupMockByAction(baseUrl, "get_vod_streams", new List<object>());
            SetupMockByAction(baseUrl, "get_series", new[] { new { name = "The Boys", series_id = 300, category_id = "3" } });
            
            // Mock Series Info
            var episodesJson = new { 
                episodes = new Dictionary<string, object> {
                    { "1", new[] { new { id = "400", episode_num = 1, title = "Pilot" } } }
                }
            };
            SetupMockByAction(baseUrl, "get_series_info", episodesJson);

            // Act
            var channels = await _service.GetChannelsAsync(baseUrl, "user3", "pass3", includeSeriesEpisodes: true);

            // Assert
            var channel = channels.First(c => c.Type == ChannelType.Series);
            Assert.Contains("The Boys", channel.Name);
            Assert.Contains("S01E01", channel.Name);
        }

        [Fact]
        public async Task GetChannelsProgressiveAsync_LoadsStreamsPerCategory()
        {
            var baseUrl = "http://progressive.com";
            var requestedStreamUrls = new List<string>();

            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } });
            SetupMockByAction(baseUrl, "get_live_categories", new[]
            {
                new { category_id = "1", category_name = "Sports" },
                new { category_id = "2", category_name = "News" }
            });
            SetupMockByAction(baseUrl, "get_vod_categories", new List<object>());
            SetupMockByAction(baseUrl, "get_series_categories", new List<object>());

            _handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.RequestUri!.AbsoluteUri.StartsWith(baseUrl) &&
                        req.RequestUri.Query.Contains("action=get_live_streams")),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
                {
                    lock (requestedStreamUrls)
                    {
                        requestedStreamUrls.Add(req.RequestUri!.AbsoluteUri);
                    }

                    object payload = req.RequestUri!.Query.Contains("category_id=1")
                        ? new[] { new { name = "ESPN", stream_id = 100, category_id = "1" } }
                        : req.RequestUri.Query.Contains("category_id=2")
                            ? new[] { new { name = "CNN", stream_id = 200, category_id = "2" } }
                            : Array.Empty<object>();

                    return Task.FromResult(new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.OK,
                        Content = new StringContent(JsonSerializer.Serialize(payload))
                    });
                });

            var loadedGroups = new List<(string Group, List<Channel> Channels)>();

            await _service.GetChannelsProgressiveAsync(
                baseUrl,
                "user",
                "pass",
                includeVod: false,
                onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
                onCategoryLoaded: (channels, group) =>
                {
                    loadedGroups.Add((group, channels));
                    return Task.CompletedTask;
                });

            Assert.Equal(2, requestedStreamUrls.Count);
            Assert.All(requestedStreamUrls, url => Assert.Contains("category_id=", url));
            Assert.Contains(loadedGroups, g => g.Group == "Sports" && g.Channels.Single().Name == "ESPN");
            Assert.Contains(loadedGroups, g => g.Group == "News" && g.Channels.Single().Name == "CNN");
        }

        [Fact]
        public async Task GetChannelsProgressiveBatchedAsync_StreamsLargeCategoryInBoundedBatches()
        {
            const string baseUrl = "http://batched.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } });
            SetupMockByAction(baseUrl, "get_live_categories", new[]
            {
                new { category_id = "1", category_name = "Large" }
            });
            SetupMockByAction(baseUrl, "get_vod_categories", Array.Empty<object>());
            SetupMockByAction(baseUrl, "get_series_categories", Array.Empty<object>());
            SetupMockByAction(
                baseUrl,
                "get_live_streams",
                Enumerable.Range(1, 1201).Select(index => new
                {
                    name = $"Channel {index}",
                    stream_id = index,
                    category_id = "1"
                }).ToArray());

            var batches = new List<(int Count, bool Completed)>();
            var probe = new RecordingPerformanceProbe();
            PerformanceTrace.Probe = probe;

            try
            {
                await _service.GetChannelsProgressiveBatchedAsync(
                    baseUrl,
                    "user",
                    "pass",
                    includeVod: false,
                    onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
                    onCategoryBatchLoaded: (channels, _, completed) =>
                    {
                        batches.Add((channels.Count, completed));
                        return Task.CompletedTask;
                    });
            }
            finally
            {
                PerformanceTrace.Probe = NullPerformanceProbe.Instance;
            }

            Assert.Equal(new[] { 500, 500, 201 }, batches.Select(batch => batch.Count));
            Assert.Equal(new[] { false, false, true }, batches.Select(batch => batch.Completed));
            Assert.Contains(probe.Events, item => item.Name == "xtream.categories.ready" && item.Value == 1);
            Assert.Equal(3, probe.Events.Count(item => item.Name == "xtream.batch.ready"));
            Assert.Contains(probe.Events, item => item.Name == "xtream.import.complete" && item.Value == 1201);
        }

        [Fact]
        public async Task GetChannelsProgressiveBatchedAsync_CategoryFailureAfterSuccessfulBatch_DoesNotThrow()
        {
            const string baseUrl = "http://partial-xtream.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } });
            SetupMockByAction(baseUrl, "get_live_categories", new[]
            {
                new { category_id = "1", category_name = "Working" },
                new { category_id = "2", category_name = "Broken" }
            });
            SetupMockByAction(baseUrl, "get_vod_categories", Array.Empty<object>());
            SetupMockByAction(baseUrl, "get_series_categories", Array.Empty<object>());

            _handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.RequestUri!.AbsoluteUri.StartsWith(baseUrl) &&
                        req.RequestUri.Query.Contains("action=get_live_streams")),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
                {
                    if (req.RequestUri!.Query.Contains("category_id=2"))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("category failed")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new[]
                        {
                            new { name = "Working Channel", stream_id = 100, category_id = "1" }
                        }))
                    });
                });

            var batches = new List<(string Category, int Count, bool Completed)>();

            await _service.GetChannelsProgressiveBatchedAsync(
                baseUrl,
                "user",
                "pass",
                includeVod: false,
                onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
                onCategoryBatchLoaded: (channels, category, completed) =>
                {
                    batches.Add((category.Name, channels.Count, completed));
                    return Task.CompletedTask;
                });

            var successfulBatch = Assert.Single(batches);
            Assert.Equal("Working", successfulBatch.Category);
            Assert.Equal(1, successfulBatch.Count);
            Assert.True(successfulBatch.Completed);
        }

        [Fact]
        public async Task GetChannelsProgressiveBatchedAsync_PersistenceFailure_IsNotSwallowedAsCategoryFailure()
        {
            const string baseUrl = "http://persistence-failure-xtream.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } });
            SetupMockByAction(baseUrl, "get_live_categories", new[]
            {
                new { category_id = "1", category_name = "Working" }
            });
            SetupMockByAction(baseUrl, "get_vod_categories", Array.Empty<object>());
            SetupMockByAction(baseUrl, "get_series_categories", Array.Empty<object>());
            SetupMockByAction(baseUrl, "get_live_streams", new[]
            {
                new { name = "Working Channel", stream_id = 100, category_id = "1" }
            });

            await Assert.ThrowsAsync<IOException>(() => _service.GetChannelsProgressiveBatchedAsync(
                baseUrl,
                "user",
                "pass",
                includeVod: false,
                onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
                onCategoryBatchLoaded: (_, _, _) => throw new IOException("disk full")));
        }

        [Fact]
        public async Task GetChannelsProgressiveAsync_CategoryFailureAfterSuccessfulCategory_DoesNotThrow()
        {
            const string baseUrl = "http://partial-xtream-legacy.com";
            SetupMockByAction(baseUrl, null, new { user_info = new { status = "Active" } });
            SetupMockByAction(baseUrl, "get_live_categories", new[]
            {
                new { category_id = "1", category_name = "Working" },
                new { category_id = "2", category_name = "Broken" }
            });
            SetupMockByAction(baseUrl, "get_vod_categories", Array.Empty<object>());
            SetupMockByAction(baseUrl, "get_series_categories", Array.Empty<object>());

            _handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.RequestUri!.AbsoluteUri.StartsWith(baseUrl) &&
                        req.RequestUri.Query.Contains("action=get_live_streams")),
                    ItExpr.IsAny<CancellationToken>())
                .Returns<HttpRequestMessage, CancellationToken>((req, _) =>
                {
                    if (req.RequestUri!.Query.Contains("category_id=2"))
                    {
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("category failed")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonSerializer.Serialize(new[]
                        {
                            new { name = "Working Channel", stream_id = 100, category_id = "1" }
                        }))
                    });
                });

            var loadedGroups = new List<(string Group, List<Channel> Channels)>();

            await _service.GetChannelsProgressiveAsync(
                baseUrl,
                "user",
                "pass",
                includeVod: false,
                onCategoriesDiscovered: (categories, _) => Task.FromResult(categories),
                onCategoryLoaded: (channels, group) =>
                {
                    loadedGroups.Add((group, channels));
                    return Task.CompletedTask;
                });

            var successfulCategory = Assert.Single(loadedGroups);
            Assert.Equal("Working", successfulCategory.Group);
            Assert.Single(successfulCategory.Channels);
            Assert.Equal("Working Channel", successfulCategory.Channels[0].Name);
        }

        [Fact]
        public async Task GetSeriesInfoAsync_HandlesEpisodesAsObject_ReturnsCorrectData()
        {
            // Arrange
            string baseUrl = "http://series-obj.com";
            var seriesId = 123L;
            var episodesJson = new
            {
                info = new { name = "Test Series" },
                seasons = new[] { new { season_number = 1, name = "Season 1" } },
                episodes = new Dictionary<string, object> {
                    { "1", new[] { new { id = "1001", episode_num = 1, title = "Ep 1" } } }
                }
            };
            SetupMockByAction(baseUrl, "get_series_info", episodesJson);

            // Act
            var result = await _service.GetSeriesInfoAsync(baseUrl, "user", "pass", seriesId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Episodes.ContainsKey("1"));
            Assert.Single(result.Episodes["1"]);
            Assert.Equal(1001, result.Episodes["1"][0].Id);
        }

        [Fact]
        public async Task GetSeriesInfoAsync_HandlesEpisodesAsArray_ReturnsCorrectData()
        {
            // Arrange
            string baseUrl = "http://series-arr.com";
            var seriesId = 456L;
            var episodesJson = new
            {
                info = new { name = "Test Series Array" },
                seasons = new[] { new { season_number = 1, name = "Season 1" } },
                episodes = new[] { new { id = "2001", episode_num = 1, title = "Ep 1" } }
            };
            SetupMockByAction(baseUrl, "get_series_info", episodesJson);

            // Act
            var result = await _service.GetSeriesInfoAsync(baseUrl, "user", "pass", seriesId);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Episodes.ContainsKey("1")); // Defaulted to "1" in our fix
            Assert.Single(result.Episodes["1"]);
            Assert.Equal(2001, result.Episodes["1"][0].Id);
        }

        private void SetupMockByAction(string baseUrl, string? action, object responseData)
        {
            var json = JsonSerializer.Serialize(responseData);
            
            _handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => 
                        req.RequestUri.AbsoluteUri.StartsWith(baseUrl) && 
                        (action == null ? !req.RequestUri.Query.Contains("action=") : req.RequestUri.Query.Contains($"action={action}"))),
                    ItExpr.IsAny<CancellationToken>()
                )
                .Returns(() => Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(json)
                }));
        }

        private sealed class RecordingPerformanceProbe : IPerformanceProbe
        {
            public bool IsEnabled => true;
            public List<(string Name, long Value, string? Scope)> Events { get; } = new();

            public void Mark(string name, long value = 0, string? scope = null)
                => Events.Add((name, value, scope));
        }
    }
}
