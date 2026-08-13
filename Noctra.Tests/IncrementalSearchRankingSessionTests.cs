using Noctra.Models;
using Noctra.Search;
using System.Runtime.CompilerServices;

namespace Noctra.Tests;

public sealed class IncrementalSearchRankingSessionTests
{
    [Fact]
    public void AppendChannels_EvaluatesEachIdentityOnlyOnceAcrossAllBuckets()
    {
        var cache = new SearchDocumentCache();
        var session = new IncrementalSearchRankingSession("dark", cache);
        var channels = new[]
        {
            Channel(1, "Dark Knight", ChannelType.VOD),
            Channel(2, "Dork Night", ChannelType.Live),
            Channel(3, "Completely Different", ChannelType.Live)
        };

        session.AppendChannels(channels, CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Equal(3, session.ChannelEvaluationCount);
        Assert.Equal(3, cache.DocumentBuildCount);
        Assert.Contains(snapshot.VodPrimary, item => item.Id == 1);
        Assert.Contains(snapshot.LiveSimilar, item => item.Id == 2);
        Assert.DoesNotContain(snapshot.LivePrimary, item => item.Id == 3);
        Assert.DoesNotContain(snapshot.LiveSimilar, item => item.Id == 3);
    }

    [Fact]
    public void AppendChannels_SecondPageScoresOnlyNewIdentityAndMergesRanking()
    {
        var session = new IncrementalSearchRankingSession("dark knight", new SearchDocumentCache());
        var first = Channel(1, "The Dark Knight Rises", ChannelType.VOD);

        session.AppendChannels(new[] { first }, CancellationToken.None);
        Assert.Equal(1, session.ChannelEvaluationCount);

        session.AppendChannels(
            new[]
            {
                first,
                Channel(2, "Dark Knight", ChannelType.VOD)
            },
            CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Equal(2, session.ChannelEvaluationCount);
        Assert.Equal(new[] { 2, 1 }, snapshot.VodPrimary.Select(item => item.Id));
    }

    [Fact]
    public void AppendSeries_ReusedSnapshotDoesNotRescoreSeries()
    {
        var session = new IncrementalSearchRankingSession("breaking", new SearchDocumentCache());
        var series = new[]
        {
            new Series
            {
                Id = 10,
                PlaylistId = 7,
                Name = "Breaking Bad",
                Seasons =
                [
                    new Season
                    {
                        Id = 1,
                        Episodes = [new Episode { Id = 1, Name = "Pilot" }]
                    }
                ]
            }
        };

        session.AppendSeries(series, CancellationToken.None);
        session.AppendSeries(series, CancellationToken.None);

        Assert.Equal(1, session.SeriesEvaluationCount);
        Assert.Single(session.CreateSnapshot().SeriesPrimary);
    }

    [Fact]
    public void ReplaceSeries_ReevaluatesChangedIdentityAndRemovesItsOldRanking()
    {
        var session = new IncrementalSearchRankingSession("breaking", new SearchDocumentCache());
        var original = new Series
        {
            Id = 10,
            PlaylistId = 7,
            Name = "Breaking Bad",
            CoverUrl = "https://images.example/breaking.jpg"
        };

        session.AppendSeries([original], CancellationToken.None);

        var updated = new Series
        {
            Id = 10,
            PlaylistId = 7,
            Name = "Better Call Saul",
            CoverUrl = "https://images.example/saul.jpg"
        };
        session.ReplaceSeries([updated], CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.DoesNotContain(snapshot.SeriesPrimary, item => item.Name == "Breaking Bad");
        Assert.DoesNotContain(snapshot.SeriesPrimary, item => item.Name == "Better Call Saul");
        Assert.Equal(2, session.SeriesEvaluationCount);
    }

    [Fact]
    public void ReplaceSeries_WhenCancelled_PreservesPreviouslyCommittedSeriesState()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var original = new Series { Id = 10, PlaylistId = 7, Name = "Dark" };
        session.AppendSeries([original], CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var replacement = new Series
        {
            Id = 10,
            PlaylistId = 7,
            Name = "Unrelated",
            Seasons =
            [
                new Season
                {
                    Id = 1,
                    Episodes = new CancellingEpisodeCollection(cancellation, cancelAfter: 2, count: 100)
                }
            ]
        };

        Assert.Throws<OperationCanceledException>(() =>
            session.ReplaceSeries([replacement], cancellation.Token));

        Assert.Contains(original, session.CreateSnapshot().SeriesPrimary);
        Assert.Equal(1, session.SeriesEvaluationCount);
    }

    [Fact]
    public void Ranking_ImageTieBreak_UsesOnlySupportedChannelCoverOrLogoUrls()
    {
        var invalidSession = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var alpha = Channel(1, "Dark Alpha", ChannelType.VOD);
        var betaWithInvalidLogo = Channel(2, "Dark Beta", ChannelType.VOD);
        betaWithInvalidLogo.LogoUrl = "n/a";
        invalidSession.AppendChannels([betaWithInvalidLogo, alpha], CancellationToken.None);

        Assert.Equal([alpha, betaWithInvalidLogo], invalidSession.CreateSnapshot().VodPrimary);

        var coverSession = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var betaWithLogo = Channel(3, "Dark Beta", ChannelType.VOD);
        betaWithLogo.LogoUrl = "https://images.example/dark.jpg";
        coverSession.AppendChannels([alpha, betaWithLogo], CancellationToken.None);

        Assert.Equal([betaWithLogo, alpha], coverSession.CreateSnapshot().VodPrimary);
    }

    [Fact]
    public void Ranking_ImageTieBreak_SeriesBackdropDoesNotCountAsDisplayImage()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var alpha = new Series { Id = 1, PlaylistId = 7, Name = "Dark Alpha" };
        var beta = new Series
        {
            Id = 2,
            PlaylistId = 7,
            Name = "Dark Beta",
            BackdropUrl = "https://images.example/backdrop.jpg"
        };

        session.AppendSeries([beta, alpha], CancellationToken.None);

        Assert.Equal([alpha, beta], session.CreateSnapshot().SeriesPrimary);
    }

    [Fact]
    public void AppendChannels_ReevaluatesExistingImageTieBreakAfterVisualEnrichment()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var beta = Channel(2, "Dark Beta", ChannelType.VOD);
        session.AppendChannels([beta], CancellationToken.None);

        beta.LogoUrl = "https://images.example/beta.jpg";
        var alpha = Channel(1, "Dark Alpha", ChannelType.VOD);
        session.AppendChannels([alpha], CancellationToken.None);

        Assert.Equal([beta, alpha], session.CreateSnapshot().VodPrimary);
    }

    [Fact]
    public void AppendChannels_ReevaluatesSeriesImageTieBreakAfterVisualEnrichment()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var alpha = new Series { Id = 1, PlaylistId = 7, Name = "Dark Alpha" };
        var beta = new Series { Id = 2, PlaylistId = 7, Name = "Dark Beta" };
        session.AppendSeries([alpha, beta], CancellationToken.None);

        beta.CoverUrl = "https://images.example/beta.jpg";
        session.AppendChannels(
            [Channel(99, "Unrelated", ChannelType.VOD)],
            CancellationToken.None);

        Assert.Equal([beta, alpha], session.CreateSnapshot().SeriesPrimary);
        Assert.Equal(2, session.SeriesEvaluationCount);
    }

    [Fact]
    public void DocumentCache_ReusesUnchangedDocumentAndRebuildsChangedFields()
    {
        var cache = new SearchDocumentCache();
        var channel = Channel(1, "Dark Knight", ChannelType.VOD);

        new IncrementalSearchRankingSession("dark", cache)
            .AppendChannels(new[] { channel }, CancellationToken.None);
        new IncrementalSearchRankingSession("knight", cache)
            .AppendChannels(new[] { channel }, CancellationToken.None);

        Assert.Equal(1, cache.DocumentBuildCount);
        Assert.Equal(1, cache.DocumentReuseCount);

        channel.Name = "Dark Knight Returns";
        new IncrementalSearchRankingSession("returns", cache)
            .AppendChannels(new[] { channel }, CancellationToken.None);

        Assert.Equal(2, cache.DocumentBuildCount);
    }

    [Fact]
    public void DocumentCache_ReusedSeriesDoesNotRenormalizeEpisodeNames()
    {
        var cache = new SearchDocumentCache();
        var series = new Series
        {
            Id = 20,
            PlaylistId = 7,
            Name = "Breaking Bad",
            Seasons =
            [
                new Season
                {
                    Id = 1,
                    Episodes =
                    [
                        new Episode { Id = 1, Name = "Pilot" },
                        new Episode { Id = 2, Name = "Cat's in the Bag" }
                    ]
                }
            ]
        };

        cache.GetOrCreate(series, CancellationToken.None);
        var normalizationsAfterBuild = cache.EpisodeNormalizationCount;
        cache.GetOrCreate(series, CancellationToken.None);

        Assert.Equal(2, normalizationsAfterBuild);
        Assert.Equal(normalizationsAfterBuild, cache.EpisodeNormalizationCount);
    }

    [Fact]
    public void AppendChannels_CancelledTokenTerminatesCooperatively()
    {
        var session = new IncrementalSearchRankingSession("channel", new SearchDocumentCache());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            session.AppendChannels(
                Enumerable.Range(1, 10_000)
                    .Select(id => Channel(id, $"Channel {id}", ChannelType.Live)),
                cancellation.Token));
        Assert.Equal(0, session.ChannelEvaluationCount);
    }

    [Fact]
    public void AppendChannels_CancelledBatch_IsAtomicButRetainsWorkTelemetry()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());

        Assert.Throws<OperationCanceledException>(() => session.AppendChannels(
            YieldOneThenCancel(cancellation), cancellation.Token));

        Assert.Equal(0, session.ChannelEvaluationCount);
        Assert.Equal(1, session.WorkItemEvaluationCount);
        Assert.Empty(session.CreateSnapshot().VodPrimary);
    }

    [Fact]
    public void AppendChannels_SeriesTypedChannelIsNotClassifiedAsVod()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());

        session.AppendChannels(
            new[] { Channel(50, "Dark episode", ChannelType.Series) },
            CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Empty(snapshot.LivePrimary);
        Assert.Empty(snapshot.LiveSimilar);
        Assert.Empty(snapshot.VodPrimary);
        Assert.Empty(snapshot.VodSimilar);
    }

    [Fact]
    public void AppendSeries_CancellationStopsDocumentEpisodeScanPromptly()
    {
        using var cancellation = new CancellationTokenSource();
        var episodes = new CancellingEpisodeCollection(cancellation, cancelAfter: 5, count: 1_000);
        var series = new Series
        {
            Id = 70,
            PlaylistId = 7,
            Name = "Unrelated",
            Seasons = [new Season { Id = 1, Episodes = episodes }]
        };
        var session = new IncrementalSearchRankingSession("missing episode", new SearchDocumentCache());

        Assert.Throws<OperationCanceledException>(() =>
            session.AppendSeries(new[] { series }, cancellation.Token));
        Assert.InRange(episodes.Visited, 5, 20);
    }

    [Fact]
    public void AppendSeries_CancelledItemCanBeRetriedInSameSession()
    {
        using var cancellation = new CancellationTokenSource();
        var series = new Series
        {
            Id = 71,
            PlaylistId = 7,
            Name = "Dark Series",
            Seasons =
            [
                new Season
                {
                    Id = 1,
                    Episodes = new CancellingEpisodeCollection(cancellation, cancelAfter: 2, count: 100)
                }
            ]
        };
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());

        Assert.Throws<OperationCanceledException>(() =>
            session.AppendSeries([series], cancellation.Token));

        series.Seasons = [];
        session.AppendSeries([series], CancellationToken.None);

        Assert.Contains(series, session.CreateSnapshot().SeriesPrimary);
        Assert.Equal(1, session.SeriesEvaluationCount);
    }

    [Theory]
    [InlineData("v\u0131k\u0131ng", "viking")]
    [InlineData("\u015fim\u015fek", "simsek")]
    [InlineData("de\u011fer", "deger")]
    [InlineData("\u00fcz\u00fcm", "uzum")]
    [InlineData("\u00d6zel \u00c7a\u011fr\u0131", "ozel cagri")]
    [InlineData("\u0130stanbul", "istanbul")]
    public void Normalize_RealTurkishCodePoints_AreConvertedToAscii(string input, string expected)
    {
        Assert.Equal(expected, SearchDocumentCache.Normalize(input));
    }

    [Fact]
    public void DocumentCache_DoesNotRetainSourceModelObjects()
    {
        var cache = new SearchDocumentCache();
        var channelReference = AddTransientChannelToCache(cache);
        var seriesReference = AddTransientSeriesToCache(cache);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(channelReference.IsAlive);
        Assert.False(seriesReference.IsAlive);
    }

    [Theory]
    [InlineData("breaking bad", "Breaking Bad", true)]
    [InlineData("break", "Breaking Bad", true)]
    [InlineData("knight", "The Dark Knight", true)]
    [InlineData("v\u0131k\u0131ng", "Vikings", true)]
    [InlineData("brekking bad", "Breaking Bad", false)]
    [InlineData("dark", "Dork Night", false)]
    public void Ranking_PreservesExactPrefixContainsTurkishTypoAndSimilarBehavior(
        string query,
        string title,
        bool primary)
    {
        var session = new IncrementalSearchRankingSession(query, new SearchDocumentCache());
        var channel = Channel(850, title, ChannelType.VOD);

        session.AppendChannels([channel], CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        if (primary)
        {
            Assert.Contains(channel, snapshot.VodPrimary);
            Assert.DoesNotContain(channel, snapshot.VodSimilar);
        }
        else
        {
            Assert.DoesNotContain(channel, snapshot.VodPrimary);
            Assert.Contains(channel, snapshot.VodSimilar);
        }
    }

    [Fact]
    public void Ranking_PreservesEpisodeIntentAndExcludesEpisodeOnlySimilarMatches()
    {
        var marked = new Series { Id = 860, PlaylistId = 7, Name = "Breaking Bad S01E01" };
        var episodeOnly = new Series
        {
            Id = 861,
            PlaylistId = 7,
            Name = "Unrelated Series",
            Seasons = [new Season { Id = 1, Episodes = [new Episode { Id = 1, Name = "Pilot" }] }]
        };

        var markedSession = new IncrementalSearchRankingSession("breaking bad s01e01", new SearchDocumentCache());
        markedSession.AppendSeries([marked], CancellationToken.None);
        Assert.Contains(marked, markedSession.CreateSnapshot().SeriesPrimary);

        var episodeSession = new IncrementalSearchRankingSession("pilot", new SearchDocumentCache());
        episodeSession.AppendSeries([episodeOnly], CancellationToken.None);
        var episodeSnapshot = episodeSession.CreateSnapshot();
        Assert.DoesNotContain(episodeOnly, episodeSnapshot.SeriesPrimary);
        Assert.DoesNotContain(episodeOnly, episodeSnapshot.SeriesSimilar);
    }

    [Fact]
    public void Ranking_PreservesSuggestionOutsideBoundedPrimaryResults()
    {
        var session = new IncrementalSearchRankingSession("dark", new SearchDocumentCache());
        var exactItems = Enumerable.Range(1, 96)
            .Select(id => Channel(900 + id, "dark", ChannelType.Live));
        var suggestion = Channel(1_100, "darkness", ChannelType.Live);

        session.AppendChannels(exactItems.Append(suggestion), CancellationToken.None);
        var snapshot = session.CreateSnapshot();

        Assert.Equal(96, snapshot.LivePrimary.Count);
        Assert.DoesNotContain(suggestion, snapshot.LivePrimary);
        Assert.Equal("darkness", snapshot.Suggestion);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static WeakReference AddTransientChannelToCache(SearchDocumentCache cache)
    {
        var channel = Channel(801, "Transient Channel", ChannelType.VOD);
        cache.GetOrCreate(channel);
        return new WeakReference(channel);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static WeakReference AddTransientSeriesToCache(SearchDocumentCache cache)
    {
        var series = new Series { Id = 802, PlaylistId = 7, Name = "Transient Series" };
        cache.GetOrCreate(series, CancellationToken.None);
        return new WeakReference(series);
    }

    private static Channel Channel(int id, string name, ChannelType type)
        => new()
        {
            Id = id,
            PlaylistId = 7,
            Name = name,
            Type = type
        };

    private static IEnumerable<Channel> YieldOneThenCancel(CancellationTokenSource cancellation)
    {
        yield return Channel(1, "Dark One", ChannelType.VOD);
        cancellation.Cancel();
        yield return Channel(2, "Dark Two", ChannelType.VOD);
    }

    private sealed class CancellingEpisodeCollection(
        CancellationTokenSource cancellation,
        int cancelAfter,
        int count) : ICollection<Episode>
    {
        public int Visited { get; private set; }
        public int Count => count;
        public bool IsReadOnly => true;

        public IEnumerator<Episode> GetEnumerator()
        {
            for (var id = 1; id <= count; id++)
            {
                Visited++;
                if (Visited == cancelAfter)
                {
                    cancellation.Cancel();
                }
                yield return new Episode { Id = id, Name = $"Episode {id}" };
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public void Add(Episode item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(Episode item) => false;
        public void CopyTo(Episode[] array, int arrayIndex) => throw new NotSupportedException();
        public bool Remove(Episode item) => throw new NotSupportedException();
    }
}
