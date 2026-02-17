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
        var seriesGroups = existingSeries
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            var parsed = ParseSeriesEpisodeInfo(channel.Name);
            var seriesName = parsed.SeriesName;
            var seasonNum = parsed.Season;
            var episodeNum = parsed.Episode;

            if (!seriesGroups.TryGetValue(seriesName, out var series))
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
                seriesGroups[seriesName] = series;
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
        if (string.IsNullOrWhiteSpace(channelName))
        {
            return ("Bilinmeyen Dizi", 1, 1);
        }

        var title = channelName.Trim();
        foreach (var regex in new[] { SxeRegex(), XRegex(), TurkishRegex(), EnglishRegex() })
        {
            var match = regex.Match(title);
            if (!match.Success)
            {
                continue;
            }

            var rawName = match.Groups["name"].Value;
            var seriesName = CleanSeriesName(rawName);
            var season = ParseSafeInt(match.Groups["season"].Value, fallback: 1);
            var episode = ParseSafeInt(match.Groups["episode"].Value, fallback: 1);
            return (seriesName, season, episode);
        }

        var seasonOnly = SeasonOnlyRegex().Match(title);
        if (seasonOnly.Success)
        {
            var rawName = seasonOnly.Groups["name"].Value;
            var seriesName = CleanSeriesName(rawName);
            var season = ParseSafeInt(seasonOnly.Groups["season"].Value, fallback: 1);
            return (seriesName, season, 1);
        }

        var fallbackName = CleanSeriesName(EpisodeTokenRegex().Replace(title, " "));
        return (fallbackName, 1, 1);
    }

    private static string CleanSeriesName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Bilinmeyen Dizi";
        }

        var cleaned = value.Trim();
        cleaned = EpisodeTokenRegex().Replace(cleaned, " ");
        cleaned = cleaned.Replace('_', ' ').Replace('.', ' ');
        cleaned = MultiSpaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|', ':');
        return string.IsNullOrWhiteSpace(cleaned) ? "Bilinmeyen Dizi" : cleaned;
    }

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
        return await _context.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();
    }
    public async Task UpdateSeriesAsync(Series series)
    {
        Series? dbSeries = null;

        if (series.Id > 0)
        {
            dbSeries = await _context.Series.FindAsync(series.Id);
        }

        if (dbSeries == null)
        {
            dbSeries = await _context.Series
                .FirstOrDefaultAsync(s =>
                    s.PlaylistId == series.PlaylistId &&
                    s.Name == series.Name);
        }

        if (dbSeries != null)
        {
            dbSeries.IsInMyList = series.IsInMyList;
            dbSeries.IsFavorite = series.IsFavorite;
            _context.Series.Update(dbSeries);
            await _context.SaveChangesAsync();
        }
    }

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss](?<season>\d{1,2})\s*[Ee](?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SxeRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?<season>\d{1,2})\s*[Xx]\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex XRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb][oö]l[uü]m\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex TurkishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EnglishRegex();

    [GeneratedRegex(@"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Ss]eason|[Ss]ezon)\s*(?<season>\d{1,2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonOnlyRegex();

    [GeneratedRegex(@"\b(?:[Ss]\d{1,2}[Ee]\d{1,3}|\d{1,2}[Xx]\d{1,3}|[Ss]ezon\s*\d{1,2}\s*[Bb][oö]l[uü]m\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|[Bb][oö]l[uü]m\s*\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeTokenRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}


