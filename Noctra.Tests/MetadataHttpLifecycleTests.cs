using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class MetadataHttpLifecycleTests
{
    [Fact]
    public async Task SearchSeriesAsync_DisposesSuccessfulResponseContent()
    {
        var content = new TrackingContent(
            "{\"results\":[{\"id\":42,\"media_type\":\"tv\",\"name\":\"Tracked series\",\"poster_path\":\"/poster.jpg\"}]}");
        using var client = new HttpClient(new StaticHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var service = new MetadataService(client);

        var result = await service.SearchSeriesAsync("Tracked series", "en-US");

        Assert.NotNull(result);
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task FetchSeriesDetailsAsync_UsesShortPerAttemptTimeoutWithoutChangingSharedClient()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        var service = new MetadataService(
            client,
            requestTimeout: TimeSpan.FromMilliseconds(50));
        var watch = Stopwatch.StartNew();

        var result = await service.FetchSeriesDetailsAsync(42, "en-US");

        Assert.Null(result);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromMinutes(3), client.Timeout);
    }

    private sealed class StaticHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(responseFactory());
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] _payload;

        public TrackingContent(string json)
        {
            _payload = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
            => stream.WriteAsync(_payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = _payload.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
