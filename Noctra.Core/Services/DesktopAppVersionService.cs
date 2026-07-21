using System.Reflection;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

/// <summary>
/// Windows (Avalonia) platformu için sürüm servisi.
/// Package veya unpackaged build'e göre doğru assembly'yi kullanır.
/// </summary>
public sealed class DesktopAppVersionService : IAppVersionService
{
    private readonly IPackageIdentityService? _packageIdentityService;

    public DesktopAppVersionService(IPackageIdentityService? packageIdentityService = null)
    {
        _packageIdentityService = packageIdentityService;
    }

    /// <summary>
    /// Ana uygulama assembly'sinden sürüm bilgisini okur.
    /// EntryAssembly kullanılır (Çalışan assembly değil!) - Core kütüphanesinin
    /// sürümünü değil, ana uygulamanın sürümünü almak için.
    /// </summary>
    public string DisplayVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    public long BuildNumber =>
        Assembly.GetEntryAssembly()?.GetName().Version?.Build ?? 0;
}
