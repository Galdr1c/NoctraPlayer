using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public partial class MediaService : IMediaService
{
    private readonly AppDbContext _context;

    public MediaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task AggregateContentAsync(int playlistId)
    {
        var channels = await _context.Channels
            .Where(c => c.PlaylistId == playlistId && c.Type == ChannelType.Series)
            .ToListAsync();

        if (!channels.Any()) return;

        var existingSeries = await _context.Series
            .Include(s => s.Seasons)
            .ThenInclude(se => se.Episodes)
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();
        var seriesGroups = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingSeries.OrderBy(s => s.Id))
        {
            var key = BuildSeriesGroupingKey(existing.Name);
            if (!seriesGroups.ContainsKey(key))
            {
                seriesGroups[key] = existing;
            }
        }

        foreach (var channel in channels)
        {
            var parsed = ParseSeriesEpisodeInfo(channel.Name);
            var seriesName = parsed.SeriesName;
            var seasonNum = parsed.Season;
            var episodeNum = parsed.Episode;
            var seriesKey = BuildSeriesGroupingKey(seriesName);

            if (!seriesGroups.TryGetValue(seriesKey, out var series))
            {
                series = new Series
                {
                    Name = seriesName,
                    PlaylistId = playlistId,
                    CoverUrl = channel.LogoUrl,
                    Genre = channel.GroupTitle,
                    IsInMyList = false,
                    IsFavorite = false
                };
                seriesGroups[seriesKey] = series;
                _context.Series.Add(series);
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
            }

            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
            if (season == null)
            {
                season = new Season { SeasonNumber = seasonNum, Series = series };
                series.Seasons.Add(season);
            }

            var existingEpisode = FindExistingEpisode(season, episodeNum, channel.Name, channel.StreamUrl);
            if (existingEpisode != null)
            {
                // Preserve watch/progress fields; only fill missing metadata.
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
                Name = channel.Name,
                EpisodeNumber = episodeNum,
                StreamUrl = channel.StreamUrl,
                CoverUrl = channel.LogoUrl,
                Season = season
            };
            season.Episodes.Add(episode);
        }

        await _context.SaveChangesAsync();
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

    public async Task<List<Series>> GetSeriesAsync(int playlistId)
    {
        var allSeries = await _context.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .AsNoTracking()
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();

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
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
    public async Task UpdateSeriesAsync(Series series)
    {
        var normalizedTargetKey = BuildSeriesGroupingKey(series.Name);
        if (string.IsNullOrWhiteSpace(normalizedTargetKey))
        {
            return;
        }

        var candidates = await _context.Series
            .Where(s => s.PlaylistId == series.PlaylistId)
            .ToListAsync();

        var toUpdate = candidates
            .Where(s => string.Equals(BuildSeriesGroupingKey(s.Name), normalizedTargetKey, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (toUpdate.Count == 0 && series.Id > 0)
        {
            var byId = await _context.Series.FindAsync(series.Id);
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

        await _context.SaveChangesAsync();
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


