using Noctra.Core.Advertising;

namespace Noctra.Tests.Advertising;

public sealed class InterstitialAdPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EligiblePlayback_IsAllowed()
    {
        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.True(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.Eligible, decision.Reason);
    }

    [Theory]
    [InlineData(true, false, false, false, AdDecisionReason.PlaybackFailed)]
    [InlineData(false, true, false, false, AdDecisionReason.PictureInPicture)]
    [InlineData(false, false, true, false, AdDecisionReason.BlockingOverlay)]
    public void PlaybackSafetyVetoesAreRespected(
        bool failed,
        bool pip,
        bool overlay,
        bool live,
        AdDecisionReason expected)
    {
        var context = EligibleContext() with
        {
            PlaybackFailed = failed,
            WasPictureInPicture = pip,
            HasBlockingOverlay = overlay,
            IsLiveContent = live
        };

        var decision = InterstitialAdPolicy.Evaluate(
            context,
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(expected, decision.Reason);
    }

    [Fact]
    public void LivePlayback_IsAllowedAfterEligibilityThreshold()
    {
        var context = EligibleContext() with { IsLiveContent = true };

        var decision = InterstitialAdPolicy.Evaluate(
            context,
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.True(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.Eligible, decision.Reason);
    }

    [Fact]
    public void LivePlayback_CanStillBeDisabledByProviderPolicy()
    {
        var context = EligibleContext() with { IsLiveContent = true };
        var options = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            AllowLiveContent = false
        };

        var decision = InterstitialAdPolicy.Evaluate(
            context,
            EligibleRuntime(),
            options,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.LiveContent, decision.Reason);
    }

    [Fact]
    public void PremiumAlwaysWins()
    {
        var runtime = EligibleRuntime() with { IsPremium = true };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            runtime,
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.Premium, decision.Reason);
    }

    [Fact]
    public void ConsentUnavailableVetoesRequest()
    {
        var runtime = EligibleRuntime() with { CanRequestAds = false };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            runtime,
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.ConsentUnavailable, decision.Reason);
    }

    [Fact]
    public void NotReadyNeverMakesNavigationWait()
    {
        var runtime = EligibleRuntime() with { AdReady = false };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            runtime,
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.AdNotReady, decision.Reason);
    }

    [Fact]
    public void ShortPlaybackIsRejected()
    {
        var context = EligibleContext() with
        {
            PlaybackDuration = TimeSpan.FromMinutes(9.9)
        };

        var decision = InterstitialAdPolicy.Evaluate(
            context,
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.PlaybackTooShort, decision.Reason);
    }

    [Fact]
    public void DownloadedPlaybackIsRejected()
    {
        var context = EligibleContext() with
        {
            IsDownloadedContent = true
        };

        var decision = InterstitialAdPolicy.Evaluate(
            context,
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.DownloadedContent, decision.Reason);
    }

    [Fact]
    public void CooldownIsRolling()
    {
        var history = new InterstitialAdHistory(
            new[] { Now - TimeSpan.FromMinutes(10) });

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            history);

        Assert.Equal(AdDecisionReason.Cooldown, decision.Reason);
    }

    [Fact]
    public void HourlyCapIsRolling()
    {
        var history = new InterstitialAdHistory(new[]
        {
            Now - TimeSpan.FromMinutes(20),
            Now - TimeSpan.FromMinutes(40)
        });

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            history);

        Assert.Equal(AdDecisionReason.HourlyCap, decision.Reason);
    }

    [Fact]
    public void DailyCapIsRolling()
    {
        var history = new InterstitialAdHistory(new[]
        {
            Now - TimeSpan.FromHours(2),
            Now - TimeSpan.FromHours(4),
            Now - TimeSpan.FromHours(6),
            Now - TimeSpan.FromHours(8)
        });

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            history);

        Assert.Equal(AdDecisionReason.DailyCap, decision.Reason);
    }

    [Fact]
    public void ZeroMaxPerHour_DeniesAll_EvenWithEmptyHistory()
    {
        var options = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            MaxPerHour = 0
        };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            options,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.HourlyCap, decision.Reason);
    }

    [Fact]
    public void ZeroMaxPerDay_DeniesAll_EvenWithEmptyHistory()
    {
        var options = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            MaxPerDay = 0
        };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            options,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.DailyCap, decision.Reason);
    }

    [Fact]
    public void NegativeCaps_DenyAll_FailClosed()
    {
        var options = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            MaxPerHour = -3,
            MaxPerDay = -3
        };

        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            options,
            InterstitialAdHistory.Empty);

        Assert.False(decision.ShouldShow);
        Assert.Equal(AdDecisionReason.HourlyCap, decision.Reason);
    }

    [Fact]
    public void ZeroCaps_DoNotAffectOtherPlacements()
    {
        var zeroCaps = AdvertisingOptions.ConservativeDefault.PlaybackExit with
        {
            MaxPerHour = 0,
            MaxPerDay = 0
        };
        var decision = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            zeroCaps,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.HourlyCap, decision.Reason);

        var normal = AdvertisingOptions.ConservativeDefault.PlaybackExit;
        var allowed = InterstitialAdPolicy.Evaluate(
            EligibleContext(),
            EligibleRuntime(),
            normal,
            InterstitialAdHistory.Empty);

        Assert.Equal(AdDecisionReason.Eligible, allowed.Reason);
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
}
