using Noctra.Threading;

namespace Noctra.Tests;

public sealed class SupersedingCancellationScopeTests
{
    [Fact]
    public void Supersede_CancelsPreviousTokenAndKeepsReplacementActive()
    {
        using var scope = new SupersedingCancellationScope();
        using var previous = scope.CreateLinkedTokenSource(
            CancellationToken.None,
            out var previousGeneration);

        var replacementGeneration = scope.Supersede();
        using var replacement = scope.CreateLinkedTokenSource(
            CancellationToken.None,
            out var observedReplacementGeneration);

        Assert.True(previous.IsCancellationRequested);
        Assert.False(replacement.IsCancellationRequested);
        Assert.False(scope.IsCurrent(previousGeneration));
        Assert.True(scope.IsCurrent(replacementGeneration));
        Assert.Equal(replacementGeneration, observedReplacementGeneration);
    }

    [Fact]
    public void Dispose_CancelsCurrentToken()
    {
        var scope = new SupersedingCancellationScope();
        using var current = scope.CreateLinkedTokenSource(
            CancellationToken.None,
            out _);

        scope.Dispose();

        Assert.True(current.IsCancellationRequested);
    }

    [Fact]
    public async Task CreateLinkedTokenSource_WhileSuperseding_DoesNotRaceDisposedSources()
    {
        using var scope = new SupersedingCancellationScope();
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();

        var readers = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 1_000; index++)
            {
                try
                {
                    using var linked = scope.CreateLinkedTokenSource(
                        CancellationToken.None,
                        out var generation);
                    if (!scope.IsCurrent(generation))
                    {
                        Assert.True(SpinWait.SpinUntil(
                            () => linked.IsCancellationRequested,
                            TimeSpan.FromSeconds(1)));
                    }
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
            }
        }));

        var superseder = Task.Run(() =>
        {
            for (var index = 0; index < 1_000; index++)
            {
                scope.Supersede();
            }
        });

        await Task.WhenAll(readers.Append(superseder));

        Assert.Empty(failures);
    }
}
