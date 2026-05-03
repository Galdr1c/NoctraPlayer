using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public class AddProfilePlaceholder { }

public partial class ProfilesViewModel : ObservableObject
{
    private const string ProfilesLimitKey = "profiles";
    private readonly IProfileService _profileService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILicenseService _licenseService; private readonly ILocalizationService _localizationService;
    private System.Timers.Timer? _countdownRefreshTimer;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    [ObservableProperty]
    private bool _canAddProfile = true;

    [ObservableProperty]
    private bool _showAddButton = true;

    [ObservableProperty]
    private bool _isCompact;

    [ObservableProperty]
    private ObservableCollection<object> _displayItems = new();

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(
        IProfileService profileService,
        IDialogService dialogService,
        IDispatcherService dispatcherService,
        ILicenseService licenseService, ILocalizationService localizationService)
    {
        _profileService = profileService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _licenseService = licenseService; _localizationService = localizationService;
    }

    public void RefreshProfiles()
    {
        // Use a discarded task to run the async method
        _ = LoadProfilesAsync();
    }

    public Task RefreshProfilesAsync()
    {
        return LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        try 
        {
            var items = await _profileService.GetProfilesAsync();
            Profiles = new ObservableCollection<Profile>(items);
            if (!items.Any()) IsManageMode = false;
            
            CanAddProfile = _licenseService.IsWithinLimit(ProfilesLimitKey, items.Count);
            UpdateShowAddButton();
            UpdateDisplayItems();
        }
        catch (Exception ex)
        {
            
            Profiles = new ObservableCollection<Profile>();
            IsManageMode = false;
        }

        StartCountdownRefreshIfNeeded();
    }

    private void StartCountdownRefreshIfNeeded()
    {
        var hasPendingDeletions = Profiles.Any(p => p.IsPendingDeletion);

        if (hasPendingDeletions && _countdownRefreshTimer == null)
        {
            _countdownRefreshTimer = new System.Timers.Timer(60_000); // 60 saniyede bir
            _countdownRefreshTimer.Elapsed += async (_, _) =>
            {
                try
                {
                    await _dispatcherService.InvokeAsync(async () =>
                    {
                        await LoadProfilesAsync();
                    });
                }
                catch { /* Ignore timer errors */ }
            };
            _countdownRefreshTimer.AutoReset = true;
            _countdownRefreshTimer.Start();
        }
        else if (!hasPendingDeletions && _countdownRefreshTimer != null)
        {
            _countdownRefreshTimer.Stop();
            _countdownRefreshTimer.Dispose();
            _countdownRefreshTimer = null;
        }
    }

    [RelayCommand]
    private void ToggleManageMode()
    {
        IsManageMode = !IsManageMode;
        UpdateShowAddButton();
        UpdateDisplayItems();
    }

    private void UpdateShowAddButton()
    {
        if (IsManageMode)
        {
            ShowAddButton = false;
            return;
        }

        // Eğer mutlak üst sınıra (12) ulaşıldıysa, butonu her durumda gizle.
        if (Profiles.Count >= TierLimits.Premium.MaxProfiles)
        {
            ShowAddButton = false;
            return;
        }

        // _localizationService.GetString("Profiles.Upsell.Notice") 
        
        if (_licenseService.CurrentTier == SubscriptionTier.Premium)
        {
            ShowAddButton = Profiles.Count < TierLimits.Premium.MaxProfiles;
        }
        else
        {
            ShowAddButton = true;
        }
    }

    private void UpdateDisplayItems()
    {
        _dispatcherService.InvokeAsync(() =>
        {
            DisplayItems.Clear();
            foreach (var profile in Profiles)
            {
                DisplayItems.Add(profile);
            }

            if (ShowAddButton)
            {
                DisplayItems.Add(new AddProfilePlaceholder());
            }

            IsCompact = DisplayItems.Count > 6;
            return Task.CompletedTask;
        });
    }

    [RelayCommand]
    private async Task AddProfile()
    {
        var profileCount = Profiles.Count;
        
        if (!_licenseService.IsWithinLimit(ProfilesLimitKey, profileCount))
        {
            // If at limit and not premium, show upsell. 
            // If already premium and at limit (12), show info dialog (though button should be hidden).
            if (_licenseService.CurrentTier != SubscriptionTier.Premium)
            {
                await _dialogService.ShowUpsellAsync();
            }
            else
            {
                await _dialogService.ShowErrorAsync(_localizationService.GetString("Profiles.Error.LimitTitle"), string.Format(_localizationService.GetString("Profiles.Error.LimitFormat"), TierLimits.Premium.MaxProfiles));
            }
            return;
        }

        OnProfileAddRequested?.Invoke(null!);
    }

    [RelayCommand]
    private async Task SelectProfile(Profile profile)
    {
        if (profile == null) return;
        
        if (IsManageMode)
        {
             // In manage mode, clicking profile edits it
             await EditProfile(profile);
             return;
        }

        // Normal mode
        await _profileService.UpdateLastUsedAsync(profile.Id);

        OnProfileSelected?.Invoke(profile);
        RequestClose?.Invoke();
    }
    
    [RelayCommand]
    private async Task DeleteProfile(Profile profile)
    {
        if (profile == null) return;
        
        // 1. Confirmation
        var confirmed = await _dialogService.ShowConfirmationAsync(_localizationService.GetString("Profiles.Delete.Title"), 
            string.Format(_localizationService.GetString("Profiles.Delete.ConfirmationFormat"), profile.Name));
            
        if (!confirmed) return;

        try
        {
            if (profile != null)
            {
                await _profileService.DeleteProfileAsync(profile.Id, profile.ProviderAccountId);
            }
            else
            {
                return;
            }

            // 4. Update UI
            var profileToRemove = Profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (profileToRemove != null)
            {
                Profiles.Remove(profileToRemove);
                if (!Profiles.Any()) IsManageMode = false;
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Common.Error"), _localizationService.GetString("Profiles.Delete.Error"), ex);
        }
    }

    [RelayCommand]
    private Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
        return Task.CompletedTask;
    }
}


