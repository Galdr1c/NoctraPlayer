using IPTVPlayer.Models;

namespace IPTVPlayer.Services.Interfaces;

public interface IMediaService
{
    Task AggregateContentAsync(int playlistId);
    Task<List<Series>> GetSeriesAsync(int playlistId);
}
