using Noctra.Core.Advertising;

namespace Noctra.Tests.Advertising;

public sealed class AdvertisingRemoteConfigParserTests
{
    [Fact]
    public void ValidJson_MapsSpacingAndMax()
    {
        const string json = """
            {
              "movies": { "spacing": 20, "max": 3 },
              "series": { "spacing": 10, "max": 4 },
              "live": { "spacing": 30, "max": 1 }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(true, 20, 3), options.Movies);
        Assert.Equal(new NativeAdPlacementOptions(true, 10, 4), options.Series);
        Assert.Equal(new NativeAdPlacementOptions(true, 30, 1), options.Live);
    }

    [Fact]
    public void ValidJson_DefaultsPreservedForMissingSections()
    {
        const string json = """{ "movies": { "spacing": 14, "max": 2 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(true, 14, 2), options.Movies);
        Assert.Equal(AdvertisingOptions.ConservativeDefault.Series, options.Series);
        Assert.Equal(AdvertisingOptions.ConservativeDefault.Live, options.Live);
        Assert.Equal(AdvertisingOptions.ConservativeDefault.Search, options.Search);
        Assert.Equal(AdvertisingOptions.ConservativeDefault.PlaybackExit, options.PlaybackExit);
    }

    [Fact]
    public void ExplicitEnabledFalse_TurnsPlacementOff()
    {
        const string json = """{ "live": { "enabled": false, "spacing": 10, "max": 5 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(false, 10, 5), options.Live);
    }

    [Fact]
    public void MissingEnabled_KeepsFallbackEnabledState_FailClosed()
    {
        const string json = """{ "home": { "spacing": 6, "max": 1 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            new NativeAdPlacementOptions(false, 6, 1),
            options.Home);
    }

    [Theory]
    [InlineData("""{ "home": { "enabled": "yes", "spacing": 6, "max": 1 } }""")]
    [InlineData("""{ "home": { "enabled": 1, "spacing": 6, "max": 1 } }""")]
    [InlineData("""{ "home": { "enabled": null, "spacing": 6, "max": 1 } }""")]
    public void NonBooleanEnabled_KeepsFallbackEnabledState_FailClosed(string json)
    {
        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            new NativeAdPlacementOptions(false, 6, 1),
            options.Home);
    }

    [Fact]
    public void ExplicitEnabledTrue_OverridesDisabledFallback()
    {
        const string json = """{ "home": { "enabled": true, "spacing": 6, "max": 1 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            new NativeAdPlacementOptions(true, 6, 1),
            options.Home);
    }

    [Fact]
    public void KillSwitchPayload_DisabledWithoutSpacingOrMax()
    {
        const string json = """{ "movies": { "enabled": false } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            new NativeAdPlacementOptions(false, 14, 2),
            options.Movies);
    }

    [Fact]
    public void KillSwitchPayload_InterstitialDisabledWithDefaultTimings()
    {
        const string json = """{ "playbackExit": { "enabled": false } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit with { Enabled = false },
            options.PlaybackExit);
    }

    [Fact]
    public void InvalidSpacing_DoesNotOverrideKillSwitch()
    {
        const string json = """{ "movies": { "enabled": false, "spacing": 0, "max": 3 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(false, 14, 3), options.Movies);
    }

    [Theory]
    [InlineData("""{ "movies": { "spacing": 5000, "max": 2 } }""")]
    [InlineData("""{ "movies": { "spacing": 3, "max": 2 } }""")]
    [InlineData("""{ "movies": { "spacing": 14, "max": 2000000000 } }""")]
    [InlineData("""{ "movies": { "spacing": 14, "max": 9 } }""")]
    public void OutOfEnvelopeValues_FallBackToDefault(string json)
    {
        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(AdvertisingOptions.ConservativeDefault.Movies, options.Movies);
    }

    [Fact]
    public void EnvelopeBoundaryValues_AreAccepted()
    {
        const string json = """
            {
              "movies": { "spacing": 100, "max": 8 },
              "home": { "spacing": 6, "max": 0 }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(true, 100, 8), options.Movies);
        Assert.Equal(new NativeAdPlacementOptions(false, 6, 0), options.Home);
    }

    [Theory]
    [InlineData("""{ "playbackExit": { "minPlaybackDurationMinutes": 200 } }""")]
    [InlineData("""{ "playbackExit": { "cooldownMinutes": 5 } }""")]
    [InlineData("""{ "playbackExit": { "maxPerDay": 100 } }""")]
    [InlineData("""{ "playbackExit": { "maxPerHour": 50 } }""")]
    [InlineData("""{ "playbackExit": { "minSessionAgeMinutes": 0 } }""")]
    public void OutOfEnvelopeInterstitialValues_FallBackToDefault(string json)
    {
        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            options.PlaybackExit);
    }

    [Fact]
    public void PartialSection_MergesFieldByField()
    {
        const string json = """
            {
              "movies": { "spacing": 30 },
              "series": { "max": 5 },
              "home": { "enabled": true }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(true, 30, 2), options.Movies);
        Assert.Equal(new NativeAdPlacementOptions(true, 14, 5), options.Series);
        Assert.Equal(new NativeAdPlacementOptions(true, 6, 1), options.Home);
    }

    [Fact]
    public void PartialInterstitial_MergesFieldByField()
    {
        const string json = """
            {
              "playbackExit": {
                "allowLive": true,
                "minSessionAgeMinutes": 12
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit with
            {
                AllowLiveContent = true,
                MinSessionAge = TimeSpan.FromMinutes(12)
            },
            options.PlaybackExit);
    }

    [Theory]
    [InlineData("""{ "movies": { "spacing": 0, "max": 2 } }""")]
    [InlineData("""{ "movies": { "spacing": -5, "max": 2 } }""")]
    [InlineData("""{ "movies": { "spacing": 14, "max": -1 } }""")]
    [InlineData("""{ "movies": { "spacing": "14", "max": 2 } }""")]
    [InlineData("""{ "movies": { "max": 2 } }""")]
    [InlineData("""{ "movies": { "spacing": 14 } }""")]
    [InlineData("""{ "movies": "bogus" }""")]
    public void InvalidSection_FallsBackToDefault(string json)
    {
        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(AdvertisingOptions.ConservativeDefault.Movies, options.Movies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("""{ "movies": { "spacing": } }""")]
    public void MalformedJson_ReturnsFalseAndDefaults(string json)
    {
        Assert.False(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(AdvertisingOptions.ConservativeDefault, options);
    }

    [Fact]
    public void ValidJson_PartialInvalidSectionKeepsOthers()
    {
        const string json = """
            {
              "movies": { "spacing": 0, "max": 2 },
              "series": { "spacing": 22, "max": 1 }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(AdvertisingOptions.ConservativeDefault.Movies, options.Movies);
        Assert.Equal(new NativeAdPlacementOptions(true, 22, 1), options.Series);
    }

    [Fact]
    public void ValidJson_InterstitialSection_MapsAllFields()
    {
        const string json = """
            {
              "playbackExit": {
                "enabled": true,
                "minSessionAgeMinutes": 7,
                "minPlaybackDurationMinutes": 12,
                "cooldownMinutes": 25,
                "maxPerHour": 3,
                "maxPerDay": 6,
                "allowLive": true
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        var expected = new InterstitialAdOptions(
            Enabled: true,
            MinSessionAge: TimeSpan.FromMinutes(7),
            MinPlaybackDuration: TimeSpan.FromMinutes(12),
            Cooldown: TimeSpan.FromMinutes(25),
            MaxPerHour: 3,
            MaxPerDay: 6,
            AllowLiveContent: true);
        Assert.Equal(expected, options.PlaybackExit);
    }

    [Fact]
    public void ValidJson_InterstitialDisabledAndNoLive()
    {
        const string json = """
            {
              "playbackExit": {
                "enabled": false,
                "minSessionAgeMinutes": 5,
                "minPlaybackDurationMinutes": 10,
                "cooldownMinutes": 18,
                "maxPerHour": 2,
                "maxPerDay": 4,
                "allowLive": false
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.False(options.PlaybackExit.Enabled);
        Assert.False(options.PlaybackExit.AllowLiveContent);
        Assert.Equal(TimeSpan.FromMinutes(18), options.PlaybackExit.Cooldown);
    }

    [Fact]
    public void MissingInterstitialSection_KeepsDefault()
    {
        const string json = """{ "movies": { "spacing": 14, "max": 2 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            options.PlaybackExit);
    }

    [Fact]
    public void ZeroCaps_AreAcceptedAndMapped()
    {
        const string json = """
            {
              "playbackExit": {
                "maxPerHour": 0,
                "maxPerDay": 0
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(0, options.PlaybackExit.MaxPerHour);
        Assert.Equal(0, options.PlaybackExit.MaxPerDay);
    }

    [Fact]
    public void MissingAllowLive_KeepsFallbackState_FailClosed()
    {
        const string json = """
            {
              "playbackExit": {
                "enabled": true,
                "minSessionAgeMinutes": 5,
                "minPlaybackDurationMinutes": 10,
                "cooldownMinutes": 18,
                "maxPerHour": 2,
                "maxPerDay": 4
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            options.PlaybackExit);
    }

    [Theory]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 0, "minPlaybackDurationMinutes": 10, "cooldownMinutes": 18, "maxPerHour": 2, "maxPerDay": 4 } }""")]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 5, "minPlaybackDurationMinutes": -1, "cooldownMinutes": 18, "maxPerHour": 2, "maxPerDay": 4 } }""")]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 5, "minPlaybackDurationMinutes": 10, "cooldownMinutes": 0, "maxPerHour": 2, "maxPerDay": 4 } }""")]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 5, "minPlaybackDurationMinutes": 10, "cooldownMinutes": 18, "maxPerHour": -1, "maxPerDay": 4 } }""")]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 5, "minPlaybackDurationMinutes": 10, "cooldownMinutes": 18, "maxPerHour": 2, "maxPerDay": -3 } }""")]
    [InlineData("""{ "playbackExit": { "enabled": true, "minSessionAgeMinutes": 5, "minPlaybackDurationMinutes": 10, "cooldownMinutes": 18, "maxPerHour": 2 } }""")]
    [InlineData("""{ "playbackExit": "bogus" }""")]
    public void InvalidInterstitialSection_FallsBackToDefault(string json)
    {
        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(
            AdvertisingOptions.ConservativeDefault.PlaybackExit,
            options.PlaybackExit);
    }

    [Fact]
    public void ComprehensiveJson_MapsEverySection()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "movies": { "enabled": true, "spacing": 21, "max": 3 },
              "series": { "enabled": true, "spacing": 9, "max": 5 },
              "live": { "enabled": true, "spacing": 33, "max": 2 },
              "search": { "enabled": true, "spacing": 12, "max": 1 },
              "home": { "enabled": false, "spacing": 6, "max": 1 },
              "playbackExit": {
                "enabled": true,
                "minSessionAgeMinutes": 8,
                "minPlaybackDurationMinutes": 15,
                "cooldownMinutes": 30,
                "maxPerHour": 4,
                "maxPerDay": 8,
                "allowLive": true
              }
            }
            """;

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        Assert.Equal(new NativeAdPlacementOptions(true, 21, 3), options.Movies);
        Assert.Equal(new NativeAdPlacementOptions(true, 9, 5), options.Series);
        Assert.Equal(new NativeAdPlacementOptions(true, 33, 2), options.Live);
        Assert.Equal(new NativeAdPlacementOptions(true, 12, 1), options.Search);
        Assert.Equal(new NativeAdPlacementOptions(false, 6, 1), options.Home);

        var expectedInterstitial = new InterstitialAdOptions(
            Enabled: true,
            MinSessionAge: TimeSpan.FromMinutes(8),
            MinPlaybackDuration: TimeSpan.FromMinutes(15),
            Cooldown: TimeSpan.FromMinutes(30),
            MaxPerHour: 4,
            MaxPerDay: 8,
            AllowLiveContent: true);
        Assert.Equal(expectedInterstitial, options.PlaybackExit);
    }

    [Fact]
    public void PlacementSnapping_UsesRemoteValues()
    {
        const string json = """{ "movies": { "spacing": 20, "max": 3 } }""";

        Assert.True(RemoteAdvertisingConfigService.TryParse(json, out var options));

        // 20 content items, 6 columns -> ceil(20/6)=4 full rows (24) between
        // ads; anchors always land on complete row boundaries.
        var anchors = AdPlacementPlanner.GetContentAnchors(6, options.Movies);
        Assert.Equal(new[] { 24, 48, 72 }, anchors);
    }
}