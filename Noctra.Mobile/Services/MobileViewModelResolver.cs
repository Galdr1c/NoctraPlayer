using System;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
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
    {
        var player = _services.GetRequiredService<PlayerViewModel>();
        var main = _services.GetRequiredService<CoreMainViewModel>();
        var playlistService = _services.GetRequiredService<IPlaylistService>();

        player.LiveChannelsLoader ??= async () =>
        {
            var playlistId =
                player.CurrentChannel?.PlaylistId > 0 ? player.CurrentChannel.PlaylistId :
                main.SelectedChannel?.PlaylistId > 0 ? main.SelectedChannel.PlaylistId :
                main.SelectedPlaylist?.Id ?? 0;

            if (playlistId <= 0)
            {
                return [];
            }

            var activeGroup = !string.IsNullOrWhiteSpace(main.SelectedGroup)
                ? main.SelectedGroup
                : player.CurrentChannel?.GroupTitle;

            return await playlistService.GetChannelsFilteredAsync(
                playlistId,
                group: activeGroup,
                type: ChannelType.Live,
                limit: 500,
                sortOrder: ChannelSortOrder.NameAsc);
        };

        return player;
    }

    public ProfilesViewModel GetProfilesViewModel()
        => _services.GetRequiredService<ProfilesViewModel>();

    public ScopedServiceLease<SettingsViewModel> CreateSettingsViewModelScope()
        => ScopedServiceLease<SettingsViewModel>.Create(_services);
}
