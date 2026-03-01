using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public partial class MediaService : IMediaService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private static readonly SemaphoreSlim _aggregateLock = new(1, 1);

    public event Action<int>? OnAggregationCompleted;

    public void RaiseAggregationCompleted(int playlistId)
    {
        OnAggregationCompleted?.Invoke(playlistId);
    }

    public MediaService(IDbContextFactory<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task AggregateContentAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        await _aggregateLock.WaitAsync(cancellationToken);
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Disable change tracker for bulk operations — massive speedup on SaveChangesAsync
            context.ChangeTracker.AutoDetectChangesEnabled = false;        
        try
        {
            var channels = await context.Channels
                .Where(c => c.PlaylistId == playlistId && c.Type == ChannelType.Series)
                .ToListAsync(cancellationToken);

            if (!channels.Any()) return;

            // Load existing series graph — needed for accurate change tracking of existing entities
            var existingSeries = await context.Series
                .Include(s => s.Seasons)
                .ThenInclude(se => se.Episodes)
                .Where(s => s.PlaylistId == playlistId)
                .ToListAsync(cancellationToken);

            // O(1) series lookup by grouping key
            var seriesGroups = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in existingSeries.OrderBy(s => s.Id))
            {
                var key = BuildSeriesGroupingKey(existing.Name);
                if (!seriesGroups.ContainsKey(key))
                {
                    seriesGroups[key] = existing;
                }
            }

            // O(1) season lookup: (seriesKey, seasonNum) -> Season
            var seasonLookup = new Dictionary<(string seriesKey, int seasonNum), Season>();
            foreach (var kvp in seriesGroups)
            {
                foreach (var sn in kvp.Value.Seasons)
                {
                    seasonLookup[(kvp.Key, sn.SeasonNumber)] = sn;
                }
            }

            // O(1) episode lookup: (seasonId, streamIdentity) -> Episode — prevents duplicate insertion
            var episodeLookup = new Dictionary<(int seasonId, string streamIdentity), Episode>();
            var episodeByNumber = new Dictionary<(int seasonId, int episodeNum), Episode>();
            foreach (var kvp in seriesGroups)
            {
                foreach (var sn in kvp.Value.Seasons)
                {
                    foreach (var ep in sn.Episodes)
                    {
                        var streamId = NormalizeStreamIdentity(ep.StreamUrl);
                        if (!string.IsNullOrWhiteSpace(streamId) && sn.Id > 0)
                        {
                            episodeLookup[(sn.Id, streamId)] = ep;
                        }
                        if (sn.Id > 0)
                        {
                            episodeByNumber.TryAdd((sn.Id, ep.EpisodeNumber), ep);
                        }
                    }
                }
            }

            foreach (var channel in channels)
            {
                var parsed = ParseSeriesEpisodeInfo(channel.Name);
                var seriesName = parsed.SeriesName;
                var seasonNum = parsed.Season;
                var episodeNum = parsed.Episode;

                if (seasonNum == 0 || episodeNum == 0)
                {
                    seasonNum = Math.Max(1, seasonNum);
                    episodeNum = Math.Max(1, episodeNum);
                }

                var seriesKey = BuildSeriesGroupingKey(seriesName);

                if (!seriesGroups.TryGetValue(seriesKey, out var series))
                {
                    series = new Series
                    {
                        Name = seriesName,
                        PlaylistId = playlistId,
                        CoverUrl = channel.LogoUrl,
                        Genre = channel.GroupTitle,
                        TmdbId = channel.TmdbId,
                        ReleaseYear = channel.ReleaseYear,
                        Rating = channel.Rating,
                        ContentRating = channel.ContentRating,
                        IsInMyList = false,
                        IsFavorite = false
                    };
                    seriesGroups[seriesKey] = series;
                    context.Series.Add(series);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(series.CoverUrl) && !string.IsNullOrWhiteSpace(channel.LogoUrl))
                    {
                        series.CoverUrl = channel.LogoUrl;
                    }

                    if (string.IsNullOrWhiteSpace(series.Genre) && !string.IsNullOrWhiteSpace(channel.GroupTitle))
                    {
                        series.Genre = channel.GroupTitle;
                    }

                    if (!series.TmdbId.HasValue && channel.TmdbId.HasValue)
                        series.TmdbId = channel.TmdbId;
                    
                    if (!series.ReleaseYear.HasValue && channel.ReleaseYear.HasValue)
                        series.ReleaseYear = channel.ReleaseYear;
                        
                    if (!series.Rating.HasValue && channel.Rating.HasValue)
                        series.Rating = channel.Rating;
                        
                    if (string.IsNullOrWhiteSpace(series.ContentRating) && !string.IsNullOrWhiteSpace(channel.ContentRating))
                        series.ContentRating = channel.ContentRating;
                }

                // O(1) season lookup instead of FirstOrDefault
                if (!seasonLookup.TryGetValue((seriesKey, seasonNum), out var season))
                {
                    season = new Season { SeasonNumber = seasonNum, Series = series };
                    series.Seasons.Add(season);
                    seasonLookup[(seriesKey, seasonNum)] = season;
                }

                // O(1) episode existence check
                var channelStreamId = NormalizeStreamIdentity(channel.StreamUrl);
                Episode? existingEpisode = null;
                
                if (!string.IsNullOrWhiteSpace(channelStreamId) && season.Id > 0)
                {
                    episodeLookup.TryGetValue((season.Id, channelStreamId), out existingEpisode);
                }
                if (existingEpisode == null && season.Id > 0)
                {
                    episodeByNumber.TryGetValue((season.Id, episodeNum), out existingEpisode);
                }
                // Fallback for new seasons (Id == 0) — linear scan on small in-memory list
                if (existingEpisode == null && season.Id == 0)
                {
                    existingEpisode = FindExistingEpisode(season, episodeNum, channel.Name, channel.StreamUrl);
                }

                if (existingEpisode != null)
                {
                    if (string.IsNullOrWhiteSpace(existingEpisode.CoverUrl) && !string.IsNullOrWhiteSpace(channel.LogoUrl))
                    {
                        existingEpisode.CoverUrl = channel.LogoUrl;
                    }

                    if (string.IsNullOrWhiteSpace(existingEpisode.Plot) && !string.IsNullOrWhiteSpace(channel.Plot))
                    {
                        existingEpisode.Plot = channel.Plot;
                    }
                    continue;
                }

                var episode = new Episode
                {
                    Name = SeriesInfoParser.CleanEpisodeTitle(channel.Name, seriesName, episodeNum),
                    EpisodeNumber = episodeNum,
                    StreamUrl = channel.StreamUrl,
                    CoverUrl = channel.LogoUrl,
                    Season = season
                };
                season.Episodes.Add(episode);
                
                // Register in lookup for future iterations
                if (!string.IsNullOrWhiteSpace(channelStreamId) && season.Id > 0)
                {
                    episodeLookup[(season.Id, channelStreamId)] = episode;
                }
            }

            context.ChangeTracker.DetectChanges();
            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
        }
        finally
        {
            _aggregateLock.Release();
        }
    }

    private static Episode? FindExistingEpisode(Season season, int episodeNumber, string? episodeName, string? streamUrl)
    {
        var streamIdentity = NormalizeStreamIdentity(streamUrl);
        if (!string.IsNullOrWhiteSpace(streamIdentity))
        {
            var byStream = season.Episodes.FirstOrDefault(e =>
                string.Equals(NormalizeStreamIdentity(e.StreamUrl), streamIdentity, StringComparison.OrdinalIgnoreCase));
            if (byStream != null)
            {
                return byStream;
            }
        }

        var nameIdentity = NormalizeEpisodeName(episodeName);
        if (!string.IsNullOrWhiteSpace(nameIdentity))
        {
            var byNumberAndName = season.Episodes.FirstOrDefault(e =>
                e.EpisodeNumber == episodeNumber &&
                string.Equals(NormalizeEpisodeName(e.Name), nameIdentity, StringComparison.OrdinalIgnoreCase));
            if (byNumberAndName != null)
            {
                return byNumberAndName;
            }
        }

        return season.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);
    }

    private static string NormalizeEpisodeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value
            .Trim()
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeStreamIdentity(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl))
        {
            return string.Empty;
        }

        var trimmed = streamUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return $"{uri.Host.ToLowerInvariant()}{uri.AbsolutePath.Trim().ToLowerInvariant()}";
        }

        var q = trimmed.IndexOf('?');
        var pathOnly = q >= 0 ? trimmed[..q] : trimmed;
        return pathOnly.Trim().ToLowerInvariant();
    }

    private static (string SeriesName, int Season, int Episode) ParseSeriesEpisodeInfo(string? channelName)
    {
        var info = SeriesInfoParser.Parse(channelName);
        return (info.SeriesName, info.Season, info.Episode);
    }

    private static string CleanSeriesName(string? value) => SeriesInfoParser.CleanSeriesName(value);

    private static int ParseSafeInt(string? value, int fallback)
    {
        if (int.TryParse(value, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        if (long.TryParse(value, out var parsedLong) && parsedLong > 0)
        {
            return parsedLong > int.MaxValue ? int.MaxValue : (int)parsedLong;
        }

        return fallback;
    }

    public async Task<List<Series>> GetSeriesAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var allSeries = await context.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .AsNoTracking()
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync(cancellationToken);

        if (allSeries.Count <= 1)
        {
            return allSeries;
        }

        var mergedByKey = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in allSeries.OrderBy(s => s.Id))
        {
            var key = BuildSeriesGroupingKey(series.Name);
            if (!mergedByKey.TryGetValue(key, out var target))
            {
                mergedByKey[key] = series;
                continue;
            }

            MergeSeriesInMemory(target, series);
        }

        return mergedByKey.Values
            .OrderBy(s => GetSortKey(s.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string GetSortKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "zzz";
        
        // Skip leading symbols/numbers to sort naturally by letters if possible
        var cleaned = name.Trim().ToLowerInvariant();
        var index = 0;
        while (index < cleaned.Length && !char.IsLetterOrDigit(cleaned[index]))
        {
            index++;
        }

        if (index >= cleaned.Length) return cleaned; // It's all symbols
        return cleaned.Substring(index);
    }
    public async Task UpdateSeriesAsync(Series series, CancellationToken cancellationToken = default)
    {
        var normalizedTargetKey = BuildSeriesGroupingKey(series.Name);
        if (string.IsNullOrWhiteSpace(normalizedTargetKey))
        {
            return;
        }

        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await context.Series
            .Where(s => s.PlaylistId == series.PlaylistId)
            .ToListAsync(cancellationToken);

        var toUpdate = candidates
            .Where(s => string.Equals(BuildSeriesGroupingKey(s.Name), normalizedTargetKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toUpdate.Count == 0 && series.Id > 0)
        {
            var byId = await context.Series.FindAsync(new object[] { series.Id }, cancellationToken);
            if (byId != null)
            {
                toUpdate.Add(byId);
            }
        }

        if (toUpdate.Count == 0)
        {
            return;
        }

        foreach (var item in toUpdate)
        {
            item.IsInMyList = series.IsInMyList;
            item.IsFavorite = series.IsFavorite;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSeriesGroupingKey(string? seriesName)
    {
        var normalized = SeriesProgressIdentity.NormalizeSeriesKey(seriesName);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        return NormalizeEpisodeName(seriesName);
    }

    private static void MergeSeriesInMemory(Series target, Series source)
    {
        if (string.IsNullOrWhiteSpace(target.CoverUrl) && !string.IsNullOrWhiteSpace(source.CoverUrl))
        {
            target.CoverUrl = source.CoverUrl;
        }

        if (string.IsNullOrWhiteSpace(target.Plot) && !string.IsNullOrWhiteSpace(source.Plot))
        {
            target.Plot = source.Plot;
        }

        if (string.IsNullOrWhiteSpace(target.Genre) && !string.IsNullOrWhiteSpace(source.Genre))
        {
            target.Genre = source.Genre;
        }

        foreach (var sourceSeason in source.Seasons)
        {
            var targetSeason = target.Seasons.FirstOrDefault(s => s.SeasonNumber == sourceSeason.SeasonNumber);
            if (targetSeason == null)
            {
                target.Seasons.Add(sourceSeason);
                continue;
            }

            foreach (var sourceEpisode in sourceSeason.Episodes)
            {
                var existingEpisode = FindExistingEpisode(
                    targetSeason,
                    sourceEpisode.EpisodeNumber,
                    sourceEpisode.Name,
                    sourceEpisode.StreamUrl);

                if (existingEpisode == null)
                {
                    targetSeason.Episodes.Add(sourceEpisode);
                    continue;
                }

                if (existingEpisode.LastWatched == null && sourceEpisode.LastWatched != null)
                {
                    existingEpisode.LastWatched = sourceEpisode.LastWatched;
                }

                if (existingEpisode.WatchedPosition == null && sourceEpisode.WatchedPosition != null)
                {
                    existingEpisode.WatchedPosition = sourceEpisode.WatchedPosition;
                }

                if (existingEpisode.Duration == null && sourceEpisode.Duration != null)
                {
                    existingEpisode.Duration = sourceEpisode.Duration;
                }

                if (string.IsNullOrWhiteSpace(existingEpisode.CoverUrl) && !string.IsNullOrWhiteSpace(sourceEpisode.CoverUrl))
                {
                    existingEpisode.CoverUrl = sourceEpisode.CoverUrl;
                }

                if (string.IsNullOrWhiteSpace(existingEpisode.Plot) && !string.IsNullOrWhiteSpace(sourceEpisode.Plot))
                {
                    existingEpisode.Plot = sourceEpisode.Plot;
                }
            }

            targetSeason.Episodes = targetSeason.Episodes
                .OrderBy(e => e.EpisodeNumber)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        target.Seasons = target.Seasons
            .OrderBy(s => s.SeasonNumber)
            .ToList();
    }

}


