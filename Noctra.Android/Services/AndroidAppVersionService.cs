using System.Reflection;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android platformu için sürüm servisi.
/// Google Play'de belirlenen versionName ve versionCode değerlerini kullanır.
/// </summary>
public sealed class AndroidAppVersionService : IAppVersionService
{
    public string DisplayVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    public long BuildNumber =>
        Assembly.GetEntryAssembly()?.GetName().Version?.Build ?? 0;
}
