using Noctra.Core.Advertising;

namespace Noctra.Tests.Advertising;

public sealed class InterstitialAdPolicyCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordedImpressionSurvivesCoordinatorRecreationAndEnforcesCooldown()
    {
        var store = new MemoryHistoryStore();
        var first = new InterstitialAdPolicyCoordinator(store);
        first.RecordImpression(Now - TimeSpan.FromMinutes(10));

        var recreated = new InterstitialAdPolicyCoordinator(store);
        var decision = recreated.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.Cooldown, decision.Reason);
    }

    [Fact]
    public void RecordingPrunesEntriesOutsideRollingDay()
    {
        var store = new MemoryHistoryStore(
            Now - TimeSpan.FromHours(25),
            Now - TimeSpan.FromHours(2));
        var coordinator = new InterstitialAdPolicyCoordinator(store);

        coordinator.RecordImpression(Now);

        Assert.Equal(
            new[] { Now - TimeSpan.FromHours(2), Now },
            store.Impressions.OrderBy(timestamp => timestamp));
    }

    [Fact]
    public void UnreadableHistoryFailsClosedWithoutThrowing()
    {
        var coordinator = new InterstitialAdPolicyCoordinator(
            new ThrowingHistoryStore());

        var decision = coordinator.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.HistoryUnavailable, decision.Reason);
    }

    [Fact]
    public void WriteFailureFailsClosedForLaterEligibilityChecks()
    {
        var coordinator = new InterstitialAdPolicyCoordinator(
            new WriteFailingHistoryStore());

        coordinator.RecordImpression(Now);
        var decision = coordinator.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.HistoryUnavailable, decision.Reason);
    }

    [Fact]
    public void PersistedHistoryEnforcesHourlyCapAcrossCoordinatorInstances()
    {
        var store = new MemoryHistoryStore(
            Now - TimeSpan.FromMinutes(50),
            Now - TimeSpan.FromMinutes(25));
        var recreated = new InterstitialAdPolicyCoordinator(store);
        var options = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            Cooldown = TimeSpan.Zero
        };

        var decision = recreated.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            options);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.HourlyCap, decision.Reason);
    }

    private static InterstitialAdContext EligibleContext() => new(
        Now: Now,
        SessionStartedAt: Now - TimeSpan.FromMinutes(30),
        PlaybackDuration: TimeSpan.FromMinutes(12),
        PlaybackEstablished: true,
        IsLiveContent: false,
        IsDownloadedContent: false,
        PlaybackFailed: false,
        WasPictureInPicture: false,
        HasBlockingOverlay: false);

    private static AdRuntimeEligibility EligibleRuntime() => new(
        IsPremium: false,
        CanRequestAds: true,
        AdReady: true);

    private sealed class MemoryHistoryStore : IInterstitialAdHistoryStore
    {
        public MemoryHistoryStore(params DateTimeOffset[] impressions)
        {
            Impressions.AddRange(impressions);
        }

        public List<DateTimeOffset> Impressions { get; } = new();

        public IReadOnlyList<DateTimeOffset> Read()
            => Impressions.ToArray();

        public void Write(IReadOnlyList<DateTimeOffset> impressions)
        {
            Impressions.Clear();
            Impressions.AddRange(impressions);
        }
    }

    private sealed class ThrowingHistoryStore : IInterstitialAdHistoryStore
    {
        public IReadOnlyList<DateTimeOffset> Read()
            => throw new IOException("history unavailable");

        public void Write(IReadOnlyList<DateTimeOffset> impressions)
            => throw new IOException("history unavailable");
    }

    private sealed class WriteFailingHistoryStore : IInterstitialAdHistoryStore
    {
        public IReadOnlyList<DateTimeOffset> Read()
            => Array.Empty<DateTimeOffset>();

        public void Write(IReadOnlyList<DateTimeOffset> impressions)
            => throw new IOException("history write failed");
    }
}
