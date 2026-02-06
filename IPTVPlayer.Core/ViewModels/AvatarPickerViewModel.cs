using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Services.Interfaces;
using System.Collections.ObjectModel;

namespace IPTVPlayer.ViewModels;

public partial class AvatarPickerViewModel : ObservableObject
{
    private readonly IAvatarService _avatarService;

    [ObservableProperty]
    private ObservableCollection<string> _avatars = new();

    [ObservableProperty]
    private string? _selectedAvatar;

    public event EventHandler<string>? AvatarSelected;

    public AvatarPickerViewModel(IAvatarService avatarService)
    {
        _avatarService = avatarService;
        LoadAvatars();
    }

    private void LoadAvatars()
    {
        var allAvatars = _avatarService.GetAvatarsByCategory();
        var flatList = allAvatars.Values.SelectMany(x => x).ToList();
        Avatars = new ObservableCollection<string>(flatList);
    }
    
    [RelayCommand]
    private void SelectAvatar(string avatar)
    {
        SelectedAvatar = avatar;
        AvatarSelected?.Invoke(this, avatar);
    }
}
