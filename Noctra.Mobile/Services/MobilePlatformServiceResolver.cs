using System;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Services.Interfaces;

namespace Noctra.Mobile.Services;

public sealed class MobilePlatformServiceResolver
{
    private readonly IServiceProvider _services;

    public MobilePlatformServiceResolver(IServiceProvider services)
    {
        _services = services;
    }

    public IVideoSurfaceService? GetVideoSurfaceService()
        => _services.GetService<IVideoSurfaceService>();

    public IPictureInPictureService? GetPictureInPictureService()
        => _services.GetService<IPictureInPictureService>();

    public IPlayerWindowService? GetPlayerWindowService()
        => _services.GetService<IPlayerWindowService>();

    public MobileBackNavigationService? GetBackNavigationService()
        => _services.GetService<MobileBackNavigationService>();
}
