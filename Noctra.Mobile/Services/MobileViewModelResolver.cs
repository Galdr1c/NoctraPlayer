using System;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Services;
using Noctra.ViewModels;
using CoreMainViewModel = Noctra.ViewModels.MainViewModel;

namespace Noctra.Mobile.Services;

public sealed class MobileViewModelResolver
{
    private readonly IServiceProvider _services;

    public MobileViewModelResolver(IServiceProvider services)
    {
        _services = services;
    }

    public CoreMainViewModel GetCoreMainViewModel()
        => _services.GetRequiredService<CoreMainViewModel>();

    public PlayerViewModel GetPlayerViewModel()
        => _services.GetRequiredService<PlayerViewModel>();

    public ProfilesViewModel GetProfilesViewModel()
        => _services.GetRequiredService<ProfilesViewModel>();

    public ScopedServiceLease<SettingsViewModel> CreateSettingsViewModelScope()
        => ScopedServiceLease<SettingsViewModel>.Create(_services);
}
