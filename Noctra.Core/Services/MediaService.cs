using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Data;
using Microsoft.EntityFrameworkCore;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class MediaService : IMediaService
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

        var seriesGroups = new Dictionary<string, Series>();

        foreach (var channel in channels)
        {
            // Regex ile dizi adını, sezonu ve bölümü ayıkla
            var match = Regex.Match(channel.Name, @"(.+?)\s*[Ss](\d{1,2})\s*[Ee](\d{1,2})|(.+?)\s*(\d{1,2})[Xx](\d{1,2})", RegexOptions.IgnoreCase);
            
            string seriesName;
            int seasonNum = 1;
            int episodeNum = 1;

            if (match.Success)
            {
                if (match.Groups[1].Success) // S01E01 format
                {
                    seriesName = match.Groups[1].Value.Trim();
                    seasonNum = ParseSafeInt(match.Groups[2].Value, fallback: 1);
                    episodeNum = ParseSafeInt(match.Groups[3].Value, fallback: 1);
                }
                else // 1x01 format
                {
                    seriesName = match.Groups[4].Value.Trim();
                    seasonNum = ParseSafeInt(match.Groups[5].Value, fallback: 1);
                    episodeNum = ParseSafeInt(match.Groups[6].Value, fallback: 1);
                }
            }
            else
            {
                seriesName = channel.Name; // Fallback
            }

            if (!seriesGroups.TryGetValue(seriesName, out var series))
            {
                series = new Series { Name = seriesName, PlaylistId = playlistId, CoverUrl = channel.LogoUrl, Genre = channel.GroupTitle };
                seriesGroups[seriesName] = series;
                _context.Series.Add(series);
            }

            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);
            if (season == null)
            {
                season = new Season { SeasonNumber = seasonNum, Series = series };
                series.Seasons.Add(season);
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
        var dbSeries = await _context.Series.FindAsync(series.Id);
        if (dbSeries != null)
        {
            dbSeries.IsInMyList = series.IsInMyList;
            _context.Series.Update(dbSeries);
            await _context.SaveChangesAsync();
        }
    }
}


