using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Avalonia.Services;

public sealed class StubEpgService : IEpgService
{
    public bool IsLoaded => false;
    public DateTime? LastUpdated => null;
    public string? LastError => null;
    public int LastChannelMapCount => 0;

    public Task LoadEpgAsync(string epgUrl, bool isPrimary, List<Channel>? channelsForMapping = null, int daysAhead = 1)
        => Task.CompletedTask;

    public Task ClearEpgAsync() => Task.CompletedTask;

    public Task<EpgProgram?> GetCurrentProgramAsync(Channel channel)
        => Task.FromResult<EpgProgram?>(null);

    public Task<List<EpgProgram>> GetProgramsAsync(string channelId, DateTime from, DateTime to)
        => Task.FromResult(new List<EpgProgram>());

    public Task<List<EpgProgram>> GetUpcomingProgramsAsync(string channelId, int count = 5)
        => Task.FromResult(new List<EpgProgram>());

    public Task<List<EpgProgram>> GetTodayProgramsAsync(string channelId)
        => Task.FromResult(new List<EpgProgram>());

    public Task<int> GetTotalProgramCountAsync() => Task.FromResult(0);

    public Task<int> GetDistinctChannelCountAsync() => Task.FromResult(0);
}

