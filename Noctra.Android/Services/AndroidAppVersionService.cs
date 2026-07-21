using System;
using Android.Content;
using Android.Content.PM;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

/// <summary>
/// Android platformu için sürüm servisi.
/// PackageManager üzerinden gerçek versionName ve versionCode değerlerini okur.
/// </summary>
public sealed class AndroidAppVersionService : IAppVersionService
{
    private readonly Context _context;

    public AndroidAppVersionService(Context context)
    {
        _context = context.ApplicationContext ?? context;
    }

    private PackageInfo? GetPackageInfo()
    {
        try
        {
            return _context.PackageManager?.GetPackageInfo(_context.PackageName!, 0);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AndroidAppVersionService] Failed to get PackageInfo: {ex.Message}");
            return null;
        }
    }

    public string DisplayVersion =>
        GetPackageInfo()?.VersionName ?? "1.0.0";

    public long BuildNumber =>
        OperatingSystem.IsAndroidVersionAtLeast(28)
            ? GetPackageInfo()?.LongVersionCode ?? 0
            : GetPackageInfo()?.VersionCode ?? 0;
}
