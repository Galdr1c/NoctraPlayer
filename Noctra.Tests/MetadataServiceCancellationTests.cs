using System.Net;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Tests;

public sealed class MetadataServiceCancellationTests
{
    [Fact]
    public async Task FetchMetadataAsync_RethrowsCallerCancellation()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.FetchMetadataAsync(
            "Cancelled movie",
            ChannelType.VOD,
            "en-US",
            cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FetchSeriesDetailsAsync_RethrowsCallerCancellation()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.FetchSeriesDetailsAsync(42, "en-US", cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task SearchSeriesAsync_RethrowsCallerCancellation()
    {
        var handler = new BlockingHandler();
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.SearchSeriesAsync("Cancelled series", "en-US", cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task DirectFallback_RethrowsCallerCancellation()
    {
        var handler = new FallbackBlockingHandler();
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.SearchSeriesAsync("Fallback cancellation", "en-US", cancellation.Token);
        await handler.FallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FetchMetadataAsync_RethrowsCancellationFromDetailSubrequest()
    {
        var handler = new StagedBlockingHandler(blockOnRequest: 2, includeGenres: false);
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.FetchMetadataAsync(
            "Detail cancellation",
            ChannelType.VOD,
            "en-US",
            cancellation.Token);
        await handler.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FetchMetadataAsync_RethrowsCancellationFromGenreSubrequest()
    {
        var handler = new StagedBlockingHandler(blockOnRequest: 3, includeGenres: true);
        using var client = new HttpClient(handler);
        var service = new MetadataService(client);
        using var cancellation = new CancellationTokenSource();

        var request = service.FetchMetadataAsync(
            "Genre cancellation",
            ChannelType.VOD,
            "en-US",
            cancellation.Token);
        await handler.BlockedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await request.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FallbackBlockingHandler : HttpMessageHandler
    {
        private int _requestCount;

        public TaskCompletionSource FallbackStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requestCount) == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            FallbackStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StagedBlockingHandler(
        int blockOnRequest,
        bool includeGenres) : HttpMessageHandler
    {
        private int _requestCount;

        public TaskCompletionSource BlockedRequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var requestNumber = Interlocked.Increment(ref _requestCount);
            if (requestNumber == blockOnRequest)
            {
                BlockedRequestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var json = requestNumber == 1
                ? includeGenres
                    ? "{\"results\":[{\"id\":42,\"media_type\":\"movie\",\"title\":\"Genre cancellation\",\"genre_ids\":[1]}]}"
                    : "{\"results\":[{\"id\":42,\"media_type\":\"movie\",\"title\":\"Detail cancellation\",\"genre_ids\":[]}]}"
                : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
