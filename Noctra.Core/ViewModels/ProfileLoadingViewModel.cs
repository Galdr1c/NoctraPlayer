using CommunityToolkit.Mvvm.ComponentModel;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class ProfileLoadingViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    [ObservableProperty]
    private Profile? _profile;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _loadingWarningMessage = string.Empty;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate = true;

    [ObservableProperty]
    private bool _hasActiveImportJob;

    [ObservableProperty]
    private string _activeImportJobStage = string.Empty;

    [ObservableProperty]
    private int _activeImportJobLiveCount;

    [ObservableProperty]
    private int _activeImportJobVodCount;

    [ObservableProperty]
    private int _activeImportJobSeriesCount;

    [ObservableProperty]
    private int _activeImportJobFailedCategoryCount;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private string _avatar = "default";

    public ProfileLoadingViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        StatusMessage = _localizationService.GetString("Common.PleaseWait");
    }

    public void SetProfile(Profile profile)
    {
        Profile = profile;
        ProfileName = profile.Name;
        Avatar = profile.Avatar;
    }
}
