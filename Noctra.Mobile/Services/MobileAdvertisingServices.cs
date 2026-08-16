using Avalonia;

namespace Noctra.Mobile.Services;

/// <summary>
/// Resolves the current <see cref="IMobileAdvertisingService"/> from the
/// application service provider. Feed and player code use this so they never
/// need a constructor dependency on the provider.
/// </summary>
public static class MobileAdvertisingServices
{
    public static IMobileAdvertisingService? TryGet()
    {
        if (Application.Current is not App app)
            return null;

        return app.EnsureServices()?.GetService(typeof(IMobileAdvertisingService))
            as IMobileAdvertisingService;
    }
}