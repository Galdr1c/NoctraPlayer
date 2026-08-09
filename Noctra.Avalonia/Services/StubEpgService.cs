using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class StubEpgService : IEpgService
{
    public bool IsLoaded => false;
    public DateTime? LastUpdated => null;
    public string? LastError => null;

    public Task<int> LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1, IProgress<EpgProgressInfo>? progress = null, bool clearBeforeSave = false, IDictionary<string, string>? headers = null, string? preferredLanguageCode = null)
        => Task.FromResult(0);

    public Task ClearEpgAsync() => Task.CompletedTask;
    public void ClearLastError() { }

    public Task<EpgProgram?> GetCurrentProgramAsync(Channel channel)
        => Task.FromResult<EpgProgram?>(null);

    public Task<Dictionary<int, EpgProgram?>> GetCurrentProgramsAsync(IEnumerable<Channel> channels)
        => Task.FromResult(new Dictionary<int, EpgProgram?>());

    public Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
        => Task.FromResult(new List<EpgProgram>());

    public Task<Dictionary<string, List<EpgProgram>>> GetProgramsBulkAsync(IEnumerable<string> channelIds, DateTime fromLocal, DateTime toLocal)
        => Task.FromResult(new Dictionary<string, List<EpgProgram>>());

    public Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
        => Task.FromResult(new List<EpgProgram>());

    public Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
        => Task.FromResult(new List<EpgProgram>());

    public Task<int> GetTotalProgramCountAsync() => Task.FromResult(0);

    public Task<int> GetDistinctChannelCountAsync() => Task.FromResult(0);

    public Task<DateTime?> GetMaxProgramEndTimeAsync() => Task.FromResult<DateTime?>(null);
    public Task VacuumAsync() => Task.CompletedTask;
}
