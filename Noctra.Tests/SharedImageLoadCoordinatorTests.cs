using Noctra.Mobile.Services;

namespace Noctra.Tests;

public sealed class SharedImageLoadCoordinatorTests
{
    [Fact]
    public async Task SameKey_UsesOneLoaderForTwoConsumers()
    {
        var coordinator = new SharedImageLoadCoordinator<string, string>(capacity: 4);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task<string> Loader(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return completion.Task;
        }

        var first = coordinator.GetOrLoadAsync("poster", Loader, CancellationToken.None);
        var second = coordinator.GetOrLoadAsync("poster", Loader, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        completion.SetResult("bitmap");

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, calls);
        Assert.All(results, result => Assert.True(result.IsAdmitted));
        Assert.All(results, result => Assert.Equal("bitmap", result.Value));
        Assert.Equal(0, coordinator.EntryCount);
    }

    [Fact]
    public async Task CancellingOneConsumer_DoesNotCancelSharedLoader()
    {
        var coordinator = new SharedImageLoadCoordinator<string, string>(capacity: 4);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstCancellation = new CancellationTokenSource();

        Task<string> Loader(CancellationToken token)
        {
            started.TrySetResult(token);
            return completion.Task;
        }

        var first = coordinator.GetOrLoadAsync("poster", Loader, firstCancellation.Token);
        var second = coordinator.GetOrLoadAsync("poster", Loader, CancellationToken.None);
        var loaderToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(loaderToken.IsCancellationRequested);

        completion.SetResult("bitmap");
        var secondResult = await second;

        Assert.True(secondResult.IsAdmitted);
        Assert.Equal("bitmap", secondResult.Value);
        Assert.Equal(1, coordinator.ConsumerCancellationCount);
        Assert.Equal(0, coordinator.UnderlyingCancellationCount);
    }

    [Fact]
    public async Task CancellingLastConsumer_CancelsSharedLoaderAndReleasesEntry()
    {
        var coordinator = new SharedImageLoadCoordinator<string, string>(capacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var consumerCancellation = new CancellationTokenSource();

        async Task<string> Loader(CancellationToken token)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return "unreachable";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                loaderCancelled.TrySetResult();
                throw;
            }
        }

        var load = coordinator.GetOrLoadAsync("poster", Loader, consumerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        consumerCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        await loaderCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, coordinator.EntryCount);
        Assert.Equal(1, coordinator.ConsumerCancellationCount);
        Assert.Equal(1, coordinator.UnderlyingCancellationCount);
    }

    [Fact]
    public async Task Capacity_RejectsNewKeyButAllowsJoiningExistingKey()
    {
        var coordinator = new SharedImageLoadCoordinator<string, string>(capacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<string> Loader(CancellationToken _)
        {
            started.TrySetResult();
            return completion.Task;
        }

        var first = coordinator.GetOrLoadAsync("poster-a", Loader, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = await coordinator.GetOrLoadAsync("poster-b", Loader, CancellationToken.None);
        var joined = coordinator.GetOrLoadAsync("poster-a", Loader, CancellationToken.None);

        Assert.False(rejected.IsAdmitted);
        Assert.Equal(1, coordinator.OverflowRejectionCount);

        completion.SetResult("bitmap");
        Assert.True((await first).IsAdmitted);
        Assert.True((await joined).IsAdmitted);
    }

    [Fact]
    public async Task CancellingLastConsumer_HoldsCapacityUntilLoaderActuallyStops()
    {
        var coordinator = new SharedImageLoadCoordinator<string, string>(capacity: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var consumerCancellation = new CancellationTokenSource();

        async Task<string> CancellationInsensitiveLoader(CancellationToken _)
        {
            started.TrySetResult();
            await releaseLoader.Task;
            return "finished";
        }

        var first = coordinator.GetOrLoadAsync(
            "poster-a",
            CancellationInsensitiveLoader,
            consumerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        consumerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        Assert.Equal(0, coordinator.EntryCount);
        Assert.Equal(1, coordinator.ActiveLoadCount);
        Assert.Equal(1, coordinator.RunningLoadCount);
        Assert.Equal(0, coordinator.QueuedLoadCount);

        var rejected = await coordinator.GetOrLoadAsync(
            "poster-b",
            _ => Task.FromResult("new"),
            CancellationToken.None);
        Assert.False(rejected.IsAdmitted);

        releaseLoader.SetResult();
        await WaitUntilAsync(() => coordinator.ActiveLoadCount == 0);
        Assert.Equal(0, coordinator.RunningLoadCount);
        Assert.Equal(0, coordinator.QueuedLoadCount);

        var admitted = await coordinator.GetOrLoadAsync(
            "poster-b",
            _ => Task.FromResult("new"),
            CancellationToken.None);
        Assert.True(admitted.IsAdmitted);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
