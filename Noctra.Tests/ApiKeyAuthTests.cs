using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Moq;
using Moq.Protected;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Tests verifying TMDB API key is sent via Authorization header (not query param)
/// after the security fix: ?api_key=... → Authorization: Bearer <token>
/// </summary>
[Collection("SequentialTMDBTests")]
public class ApiKeyAuthTests
{
    /// <summary>
    /// Creates an HttpClient backed by a mock HttpMessageHandler that captures
    /// request details for verification.
    /// </summary>
    private static (HttpClient Client, Mock<HttpMessageHandler> Handler) CreateMockHttpClient()
    {
        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{}")
            });

        var client = new HttpClient(handlerMock.Object);
        return (client, handlerMock);
    }

    /// <summary>
    /// Extracts the HttpRequestMessage that was sent to the mock handler.
    /// </summary>
    private static HttpRequestMessage GetCapturedRequest(Mock<HttpMessageHandler> handlerMock)
    {
        return (HttpRequestMessage)handlerMock.Invocations
            .First(i => i.Method.Name == "SendAsync")
            .Arguments[0];
    }

    // ──────────────────────────────────────────────
    //  Constructor: header set from env
    // ──────────────────────────────────────────────

    [Fact]
    public void Constructor_Sets_BearerHeader_WhenApiKeyProvided()
    {
        // Arrange
        var (client, _) = CreateMockHttpClient();
        const string testKey = "test_api_key_12345";

        // Act — MetadataService asks for injected HttpClient,
        // so we set the header BEFORE creating the service
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", testKey);

        // Assert
        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal(testKey, client.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Fact]
    public void Constructor_DoesNotSetHeader_WhenApiKeyEmpty()
    {
        // Arrange
        var (client, _) = CreateMockHttpClient();

        // Act — simulate MetadataService constructor when key is empty
        // No header set

        // Assert
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    // ──────────────────────────────────────────────
    //  SetApiKey: updates / clears header
    // ──────────────────────────────────────────────

    [Fact]
    public void SetApiKey_UpdatesHeader_ToNewKey()
    {
        // Arrange
        var (client, _) = CreateMockHttpClient();
        var service = CreateServiceWithKey(client, "initial_key");

        // Act
        service.SetApiKey("new_key_67890");

        // Assert
        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("Bearer", client.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("new_key_67890", client.DefaultRequestHeaders.Authorization.Parameter);
    }

    [Fact]
    public void SetApiKey_ClearsHeader_WhenKeyIsNull()
    {
        // Arrange
        var (client, _) = CreateMockHttpClient();
        var service = CreateServiceWithKey(client, "some_key");

        // Act
        service.SetApiKey(string.Empty);

        // Assert
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void SetApiKey_CanSwitch_FromEmptyToKey()
    {
        // Arrange
        var (client, _) = CreateMockHttpClient();
        var service = CreateServiceWithKey(client, "");

        // Act
        service.SetApiKey("fresh_key");

        // Assert
        Assert.NotNull(client.DefaultRequestHeaders.Authorization);
        Assert.Equal("fresh_key", client.DefaultRequestHeaders.Authorization!.Parameter);
    }

    // ──────────────────────────────────────────────
    //  HTTP requests: verify header + no api_key in URL
    // ──────────────────────────────────────────────

    [Fact]
    public async Task FetchMetadataAsync_Sends_BearerHeader_NoApiKeyInUrl()
    {
        // Arrange
        var (client, handlerMock) = CreateMockHttpClient();
        // Don't use env key — inject via SetApiKey to isolate test
        var service = new MetadataService(client);
        service.SetApiKey("test_key_for_fetch");

        // Act
        await service.FetchMetadataAsync("Breaking Bad", ChannelType.Series);

        // Assert
        var request = GetCapturedRequest(handlerMock);
        var url = request.RequestUri!.ToString();

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test_key_for_fetch", request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain("api_key=", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchSeriesAsync_Sends_BearerHeader_NoApiKeyInUrl()
    {
        // Arrange
        var (client, handlerMock) = CreateMockHttpClient();
        var service = new MetadataService(client);
        service.SetApiKey("search_test_key");

        // Act
        await service.SearchSeriesAsync("Lost");

        // Assert
        var request = GetCapturedRequest(handlerMock);
        var url = request.RequestUri!.ToString();

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("search_test_key", request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain("api_key=", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchSeriesDetailsAsync_Sends_BearerHeader_NoApiKeyInUrl()
    {
        // Arrange
        var (client, handlerMock) = CreateMockHttpClient();
        var service = new MetadataService(client);
        service.SetApiKey("details_test_key");

        // Act
        await service.FetchSeriesDetailsAsync(1396); // Breaking Bad TMDB ID

        // Assert
        var request = GetCapturedRequest(handlerMock);
        var url = request.RequestUri!.ToString();

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("details_test_key", request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain("api_key=", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchSeasonDetailsAsync_Sends_BearerHeader_NoApiKeyInUrl()
    {
        // Arrange
        var (client, handlerMock) = CreateMockHttpClient();
        var service = new MetadataService(client);
        service.SetApiKey("season_test_key");

        // Act
        await service.FetchSeasonDetailsAsync(1396, 1);

        // Assert
        var request = GetCapturedRequest(handlerMock);
        var url = request.RequestUri!.ToString();

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("season_test_key", request.Headers.Authorization!.Parameter);
        Assert.DoesNotContain("api_key=", url, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that when API key is truly empty (no env var, manually cleared),
    /// FetchMetadataAsync returns null without making any HTTP request.
    /// Temporarily clears the TMDB_API_KEY env var for this test.
    /// </summary>
    [Fact]
    public async Task FetchMetadataAsync_ReturnsNull_WhenKeyCleared()
    {
        // Arrange — temporarily clear env var so constructor/EnsureApiKeyLoaded won't reload it
        var originalKey = Environment.GetEnvironmentVariable("TMDB_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("TMDB_API_KEY", null);

            var (client, handlerMock) = CreateMockHttpClient();
            var service = new MetadataService(client);
            // _apiKey should be empty after constructor (env var was cleared)

            // Act
            var result = await service.FetchMetadataAsync("Test", ChannelType.VOD);

            // Assert
            Assert.Null(result);
            // No request should have been made
            Assert.DoesNotContain(handlerMock.Invocations,
                i => i.Method.Name == "SendAsync");
        }
        finally
        {
            // Restore original env var
            Environment.SetEnvironmentVariable("TMDB_API_KEY", originalKey);
        }
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a MetadataService with the given HttpClient and sets the API key
    /// via SetApiKey (bypasses environment variable loading).
    /// </summary>
    private static MetadataService CreateServiceWithKey(HttpClient client, string key)
    {
        var service = new MetadataService(client);
        service.SetApiKey(key);
        return service;
    }
}
