using Moq;
using Moq.Protected;
using Noctra.Models;
using Noctra.Services;
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
        private readonly XtreamCodesService _service;

        public XtreamCodesServiceIntegrationTests()
        {
            _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            _httpClient = new HttpClient(_handlerMock.Object);
            _service = new XtreamCodesService(_httpClient);
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
    }
}
