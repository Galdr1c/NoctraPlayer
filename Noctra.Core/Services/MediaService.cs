using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;
using Noctra.Services.Interfaces;
using System.Collections.Concurrent;

namespace Noctra.Services;

public partial class MediaService : IMediaService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDispatcherService _dispatcherService;
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _aggregateLocks = new();

    public event Action<int>? OnAggregationCompleted;

    public void RaiseAggregationCompleted(int playlistId)
    {
        // Thread safety: Ensure the event is raised on the UI thread to prevent UI-bound handlers from crashing
        _dispatcherService.BeginInvoke(() => OnAggregationCompleted?.Invoke(playlistId));
    }

    public MediaService(IDbContextFactory<AppDbContext> contextFactory, IDispatcherService dispatcherService)
    {
        _contextFactory = contextFactory;
        _dispatcherService = dispatcherService;
    }

    public async Task AggregateContentAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        // Per-playlist lock prevents global bottlenecks while ensuring data integrity for specific playlists
        var gate = _aggregateLocks.GetOrAdd(playlistId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            // Disable change tracker for high-volume initial processing
            context.ChangeTracker.AutoDetectChangesEnabled = false;        

            var channels = await context.Channels
                .Where(c => c.PlaylistId == playlistId && c.Type == ChannelType.Series)
                .ToListAsync(cancellationToken);

            if (!channels.Any()) return;

            // Load existing series graph
            var existingSeries = await context.Series
                .Include(s => s.Seasons)
                .ThenInclude(se => se.Episodes)
                .Where(s => s.PlaylistId == playlistId)
                .ToListAsync(cancellationToken);

            // 1. User Data Backup: Prevent losing Favorite/MyList status if a series is temporarily removed/recreated
            var seriesUserDataMap = new Dictionary<string, (bool IsFavorite, bool IsInMyList)>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in existingSeries)
            {
                var key = BuildSeriesGroupingKey(s.Name, s.GroupTitle);
                if (!seriesUserDataMap.TryGetValue(key, out var current))
                {
                    seriesUserDataMap[key] = (s.IsFavorite, s.IsInMyList);
                }
                else
                {
                    // Merge: If any version is favorite, keep it as favorite
                    seriesUserDataMap[key] = (current.IsFavorite || s.IsFavorite, current.IsInMyList || s.IsInMyList);
                }
            }

            // O(1) series lookup by grouping key (Name + GroupTitle)
            var seriesGroups = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
            foreach (var existing in existingSeries.OrderBy(s => s.Id))
            {
                var key = BuildSeriesGroupingKey(existing.Name, existing.GroupTitle);
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

            // O(1) episode lookup: (seasonId, streamIdentity) -> Episode
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

            var mappedEpisodeIds = new HashSet<int>();

            foreach (var channel in channels)
            {
                var parsed = ParseSeriesEpisodeInfo(channel.Name);
                var seriesName = parsed.SeriesName;
                var seasonNum = parsed.Season;
                var episodeNum = parsed.Episode;

                // Robust fallback for parsing failures: Avoid overwriting real S01E01 with unknown content
                if (seasonNum <= 0 && episodeNum <= 0)
                {
                    seasonNum = 99; // 'Unknown' bucket
                    episodeNum = Math.Abs(channel.Name?.GetHashCode() ?? 0) % 1000 + 1000;
                }
                else if (seasonNum <= 0) seasonNum = 1;
                else if (episodeNum <= 0) episodeNum = 1;

                var seriesKey = BuildSeriesGroupingKey(seriesName, channel.GroupTitle);

                if (!seriesGroups.TryGetValue(seriesKey, out var series))
                {
                    seriesUserDataMap.TryGetValue(seriesKey, out var userData);
                    
                    series = new Series
                    {
                        Name = seriesName,
                        PlaylistId = playlistId,
                        CoverUrl = channel.LogoUrl,
                        GroupTitle = channel.GroupTitle,
                        Genre = channel.GroupTitle,
                        TmdbId = channel.TmdbId,
                        ReleaseYear = channel.ReleaseYear,
                        Rating = channel.Rating,
                        ContentRating = channel.ContentRating,
                        IsFavorite = userData.IsFavorite, // Restore from backup
                        IsInMyList = userData.IsInMyList  // Restore from backup
                    };
                    seriesGroups[seriesKey] = series;
                    context.Series.Add(series);
                }
                else
                {
                    // Update metadata if provider version is better/newer
                    if (string.IsNullOrWhiteSpace(series.CoverUrl) && !string.IsNullOrWhiteSpace(channel.LogoUrl))
                        series.CoverUrl = channel.LogoUrl;

                    if (!string.IsNullOrEmpty(channel.GroupTitle))
                        series.GroupTitle = channel.GroupTitle;

                    if (!series.TmdbId.HasValue && channel.TmdbId.HasValue)
                        series.TmdbId = channel.TmdbId;
                    
                    if (!series.ReleaseYear.HasValue && channel.ReleaseYear.HasValue)
                        series.ReleaseYear = channel.ReleaseYear;
                        
                    if (!series.Rating.HasValue && channel.Rating.HasValue)
                        series.Rating = channel.Rating;
                        
                    if (string.IsNullOrWhiteSpace(series.ContentRating) && !string.IsNullOrWhiteSpace(channel.ContentRating))
                        series.ContentRating = channel.ContentRating;
                }

                if (!seasonLookup.TryGetValue((seriesKey, seasonNum), out var season))
                {
                    season = new Season { SeasonNumber = seasonNum, Series = series };
                    series.Seasons.Add(season);
                    seasonLookup[(seriesKey, seasonNum)] = season;
                }

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
                if (existingEpisode == null && season.Id == 0)
                {
                    existingEpisode = FindExistingEpisode(season, episodeNum, channel.Name, channel.StreamUrl);
                }

                if (existingEpisode != null)
                {
                    // Existing episode: update metadata
                    if (string.IsNullOrWhiteSpace(existingEpisode.CoverUrl) && !string.IsNullOrWhiteSpace(channel.LogoUrl))
                        existingEpisode.CoverUrl = channel.LogoUrl;

                    if (string.IsNullOrWhiteSpace(existingEpisode.Plot) && !string.IsNullOrWhiteSpace(channel.Plot))
                        existingEpisode.Plot = channel.Plot;
                    
                    if (existingEpisode.Id > 0) mappedEpisodeIds.Add(existingEpisode.Id);
                    continue;
                }

                // New episode: ensure all metadata is assigned immediately
                var episode = new Episode
                {
                    Name = SeriesInfoParser.CleanEpisodeTitle(channel.Name, seriesName, episodeNum),
                    EpisodeNumber = episodeNum,
                    StreamUrl = channel.StreamUrl,
                    CoverUrl = channel.LogoUrl,
                    Plot = channel.Plot,
                    Duration = channel.Duration,
                    Season = season
                };
                season.Episodes.Add(episode);
                
                if (!string.IsNullOrWhiteSpace(channelStreamId) && season.Id > 0)
                {
                    episodeLookup[(season.Id, channelStreamId)] = episode;
                }
            }

            // Sync everything to DB
            context.ChangeTracker.DetectChanges();
            await context.SaveChangesAsync(cancellationToken);

            // 2. Orphan Cleanup Phase
            // Clear tracker to avoid conflicts between raw SQL deletions and tracked entities
            context.ChangeTracker.Clear();

            // A. Purge orphan episodes (no longer in provider list)
            var allEpisodesInPlaylist = await context.Episodes
                .Where(e => e.Season.Series.PlaylistId == playlistId)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            var toDeleteEpisodeIds = allEpisodesInPlaylist.Except(mappedEpisodeIds).ToList();
            if (toDeleteEpisodeIds.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaService] Purging {toDeleteEpisodeIds.Count} orphan episodes.");
                await context.Episodes
                    .Where(e => toDeleteEpisodeIds.Contains(e.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            // B. Purge Ghost Seasons (empty seasons)
            await context.Seasons
                .Where(sn => sn.Series.PlaylistId == playlistId && !sn.Episodes.Any())
                .ExecuteDeleteAsync(cancellationToken);

            // C. Purge Ghost Series (empty series)
            // IMPORTANT: Series with user data (Favorite/MyList) are KEPT even if empty to prevent data loss 
            // during temporary provider outages.
            var emptySeries = await context.Series
                .Where(s => s.PlaylistId == playlistId && 
                           !s.IsFavorite && 
                           !s.IsInMyList && 
                           !s.Seasons.Any(sn => sn.Episodes.Any()))
                .ToListAsync(cancellationToken);

            if (emptySeries.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaService] Purging {emptySeries.Count} ghost series.");
                context.Series.RemoveRange(emptySeries);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static Episode? FindExistingEpisode(Season season, int episodeNumber, string? episodeName, string? streamUrl)
    {
        var streamIdentity = NormalizeStreamIdentity(streamUrl);
        if (!string.IsNullOrWhiteSpace(streamIdentity))
        {
            var byStream = season.Episodes.FirstOrDefault(e =>
                string.Equals(NormalizeStreamIdentity(e.StreamUrl), streamIdentity, StringComparison.OrdinalIgnoreCase));
            if (byStream != null) return byStream;
        }

        var nameIdentity = NormalizeEpisodeName(episodeName);
        if (!string.IsNullOrWhiteSpace(nameIdentity))
        {
            var byNumberAndName = season.Episodes.FirstOrDefault(e =>
                e.EpisodeNumber == episodeNumber &&
                string.Equals(NormalizeEpisodeName(e.Name), nameIdentity, StringComparison.OrdinalIgnoreCase));
            if (byNumberAndName != null) return byNumberAndName;
        }

        return season.Episodes.FirstOrDefault(e => e.EpisodeNumber == episodeNumber);
    }

    private static string NormalizeEpisodeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return string.Join(" ", value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeStreamIdentity(string? streamUrl)
    {
        if (string.IsNullOrWhiteSpace(streamUrl)) return string.Empty;

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

    public async Task<List<Series>> GetSeriesAsync(int playlistId, CancellationToken cancellationToken = default)
    {
        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var allSeries = await context.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .AsNoTracking()
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync(cancellationToken);

        if (allSeries.Count <= 1) return allSeries;

        var mergedByKey = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        foreach (var series in allSeries.OrderBy(s => s.Id))
        {
            var key = BuildSeriesGroupingKey(series.Name, series.GroupTitle);
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
        
        var cleaned = name.Trim().ToLowerInvariant();
        var index = 0;
        while (index < cleaned.Length && !char.IsLetterOrDigit(cleaned[index]))
        {
            index++;
        }

        if (index >= cleaned.Length) return cleaned;
        return cleaned.Substring(index);
    }

    public async Task UpdateSeriesAsync(Series series, CancellationToken cancellationToken = default)
    {
        var normalizedTargetKey = BuildSeriesGroupingKey(series.Name, series.GroupTitle);
        if (string.IsNullOrWhiteSpace(normalizedTargetKey)) return;

        using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        // Targeted query using name prefix to avoid loading entire playlist series into memory
        var candidates = await context.Series
            .Where(s => s.PlaylistId == series.PlaylistId && s.Name.Contains(series.Name.Substring(0, Math.Min(3, series.Name.Length))))
            .ToListAsync(cancellationToken);

        var toUpdate = candidates
            .Where(s => string.Equals(BuildSeriesGroupingKey(s.Name, s.GroupTitle), normalizedTargetKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toUpdate.Count == 0 && series.Id > 0)
        {
            var byId = await context.Series.FindAsync(new object[] { series.Id }, cancellationToken);
            if (byId != null) toUpdate.Add(byId);
        }

        if (toUpdate.Count == 0) return;

        foreach (var item in toUpdate)
        {
            item.IsInMyList = series.IsInMyList;
            item.IsFavorite = series.IsFavorite;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSeriesGroupingKey(string? seriesName, string? groupTitle)
    {
        var key = SeriesProgressIdentity.NormalizeSeriesKey(seriesName);
        if (string.IsNullOrWhiteSpace(key))
        {
            key = NormalizeEpisodeName(seriesName);
        }

        if (!string.IsNullOrEmpty(groupTitle))
        {
            key += $"|{NormalizeIdentityToken(groupTitle)}";
        }

        return key;
    }

    private static string NormalizeIdentityToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return string.Join(" ", raw.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void MergeSeriesInMemory(Series target, Series source)
    {
        // Only merge if target field is empty
        if (string.IsNullOrWhiteSpace(target.CoverUrl) && !string.IsNullOrWhiteSpace(source.CoverUrl))
            target.CoverUrl = source.CoverUrl;

        if (string.IsNullOrWhiteSpace(target.Plot) && !string.IsNullOrWhiteSpace(source.Plot))
            target.Plot = source.Plot;

        if (string.IsNullOrWhiteSpace(target.Genre) && !string.IsNullOrWhiteSpace(source.Genre))
            target.Genre = source.Genre;

        if (string.IsNullOrWhiteSpace(target.GroupTitle) && !string.IsNullOrWhiteSpace(source.GroupTitle))
            target.GroupTitle = source.GroupTitle;

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
                    existingEpisode.LastWatched = sourceEpisode.LastWatched;

                if (existingEpisode.WatchedPosition == null && sourceEpisode.WatchedPosition != null)
                    existingEpisode.WatchedPosition = sourceEpisode.WatchedPosition;

                if (existingEpisode.Duration == null && sourceEpisode.Duration != null)
                    existingEpisode.Duration = sourceEpisode.Duration;

                if (string.IsNullOrWhiteSpace(existingEpisode.CoverUrl) && !string.IsNullOrWhiteSpace(sourceEpisode.CoverUrl))
                    existingEpisode.CoverUrl = sourceEpisode.CoverUrl;

                if (string.IsNullOrWhiteSpace(existingEpisode.Plot) && !string.IsNullOrWhiteSpace(sourceEpisode.Plot))
                    existingEpisode.Plot = sourceEpisode.Plot;
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
