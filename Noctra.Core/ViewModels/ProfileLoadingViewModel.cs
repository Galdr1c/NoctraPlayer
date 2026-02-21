using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;

namespace Noctra.ViewModels;

public partial class ProfileLoadingViewModel : ObservableObject
{
    [ObservableProperty]
    private Profile? _profile;

    [ObservableProperty]
    private string _statusMessage = "Lütfen bekleyin...";

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _avatar = "default";

    public ProfileLoadingViewModel()
    {
    }

    public void SetProfile(Profile profile)
    {
        Profile = profile;
        ProfileName = profile.Name;
        Avatar = profile.Avatar;
    }
}
