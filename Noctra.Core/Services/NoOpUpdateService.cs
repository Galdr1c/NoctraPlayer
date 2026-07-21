using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Debug veya unpackaged Windows build'leri için güncelleme servisi.
/// Hiçbir güncelleme kontrolü yapmaz, her zaman "desteklenmiyor" sonucu döner.
/// Böylece geliştirme build'lerinde popup çıkmaz.
/// </summary>
public sealed class NoOpUpdateService : IAppUpdateService
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new UpdateCheckResult
        {
            Status = UpdateCheckStatus.Unsupported
        });
    }

    public Task<bool> StartUpdateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<bool> CompleteUpdateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<UpdateCheckResult> CheckPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new UpdateCheckResult
        {
            Status = UpdateCheckStatus.Unsupported
        });
    }
}
