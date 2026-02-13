using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class MediaService : IMediaService
{
    private readonly AppDbContext _context;
    private static readonly Regex SxeRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)[Ss](?<season>\d{1,2})\s*[Ee](?<episode>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex XRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)(?<season>\d{1,2})\s*[Xx]\s*(?<episode>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TurkishRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]ezon\s*(?<season>\d{1,2}).*?[Bb][oö]l[uü]m\s*(?<episode>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EnglishRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)[Ss]eason\s*(?<season>\d{1,2}).*?[Ee]pisode\s*(?<episode>\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SeasonOnlyRegex = new(
        @"^(?<name>.+?)\s*(?:[-._ ]*)(?:[Ss]eason|[Ss]ezon)\s*(?<season>\d{1,2})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EpisodeTokenRegex = new(
        @"\b(?:[Ss]\d{1,2}[Ee]\d{1,3}|\d{1,2}[Xx]\d{1,3}|[Ss]ezon\s*\d{1,2}\s*[Bb][oö]l[uü]m\s*\d{1,3}|[Ee]p(?:isode)?\s*\d{1,3}|[Bb][oö]l[uü]m\s*\d{1,3})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);

    public MediaService(AppDbContext context)
    {
        _context = context;
    }

    public async Task AggregateContentAsync(int playlistId)
    {
        var staleSeries = await _context.Series
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();
        var myListStateByName = staleSeries
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Any(s => s.IsInMyList),
                StringComparer.OrdinalIgnoreCase);
        if (staleSeries.Count > 0)
        {
            _context.Series.RemoveRange(staleSeries);
            await _context.SaveChangesAsync();
        }

        var channels = await _context.Channels
            .Where(c => c.PlaylistId == playlistId && c.Type == ChannelType.Series)
            .ToListAsync();

        if (!channels.Any()) return;

        var seriesGroups = new Dictionary<string, Series>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in channels)
        {
            var parsed = ParseSeriesEpisodeInfo(channel.Name);
            var seriesName = parsed.SeriesName;
            var seasonNum = parsed.Season;
            var episodeNum = parsed.Episode;

            if (!seriesGroups.TryGetValue(seriesName, out var series))
            {
                myListStateByName.TryGetValue(seriesName, out var inMyList);
                series = new Series
                {
                    Name = seriesName,
                    PlaylistId = playlistId,
                    CoverUrl = channel.LogoUrl,
                    Genre = channel.GroupTitle,
                    IsInMyList = inMyList
                };
                seriesGroups[seriesName] = series;
                _context.Series.Add(series);
            }

            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
            if (season == null)
            {
                season = new Season { SeasonNumber = seasonNum, Series = series };
                series.Seasons.Add(season);
            }

            var hasDuplicateEpisode = season.Episodes.Any(e =>
                e.EpisodeNumber == episodeNum &&
                string.Equals(e.StreamUrl, channel.StreamUrl, StringComparison.OrdinalIgnoreCase));
            if (hasDuplicateEpisode)
            {
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

    private static (string SeriesName, int Season, int Episode) ParseSeriesEpisodeInfo(string? channelName)
    {
        if (string.IsNullOrWhiteSpace(channelName))
        {
            return ("Bilinmeyen Dizi", 1, 1);
        }

        var title = channelName.Trim();
        foreach (var regex in new[] { SxeRegex, XRegex, TurkishRegex, EnglishRegex })
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

        var seasonOnly = SeasonOnlyRegex.Match(title);
        if (seasonOnly.Success)
        {
            var rawName = seasonOnly.Groups["name"].Value;
            var seriesName = CleanSeriesName(rawName);
            var season = ParseSafeInt(seasonOnly.Groups["season"].Value, fallback: 1);
            return (seriesName, season, 1);
        }

        var fallbackName = CleanSeriesName(EpisodeTokenRegex.Replace(title, " "));
        return (fallbackName, 1, 1);
    }

    private static string CleanSeriesName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Bilinmeyen Dizi";
        }

        var cleaned = value.Trim();
        cleaned = EpisodeTokenRegex.Replace(cleaned, " ");
        cleaned = cleaned.Replace('_', ' ').Replace('.', ' ');
        cleaned = MultiSpaceRegex.Replace(cleaned, " ").Trim(' ', '-', '|', ':');
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
            _context.Series.Update(dbSeries);
            await _context.SaveChangesAsync();
        }
    }
}


