using System.Text.RegularExpressions;
using IPTVPlayer.Models;
using IPTVPlayer.Data;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.Services;

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
                    seasonNum = int.Parse(match.Groups[2].Value);
                    episodeNum = int.Parse(match.Groups[3].Value);
                }
                else // 1x01 format
                {
                    seriesName = match.Groups[4].Value.Trim();
                    seasonNum = int.Parse(match.Groups[5].Value);
                    episodeNum = int.Parse(match.Groups[6].Value);
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

    public async Task<List<Series>> GetSeriesAsync(int playlistId)
    {
        return await _context.Series
            .Include(s => s.Seasons)
            .ThenInclude(sn => sn.Episodes)
            .Where(s => s.PlaylistId == playlistId)
            .ToListAsync();
    }
}
