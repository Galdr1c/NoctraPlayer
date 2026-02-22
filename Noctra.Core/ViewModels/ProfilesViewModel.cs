using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private const string ProfilesLimitKey = "profiles";
    private readonly IProfileService _profileService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILicenseService _licenseService;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    [ObservableProperty]
    private bool _canAddProfile = true;

    [ObservableProperty]
    private bool _showAddButton = true;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(
        IProfileService profileService,
        IDialogService dialogService,
        IDispatcherService dispatcherService,
        ILicenseService licenseService)
    {
        _profileService = profileService;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _licenseService = licenseService;
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profil yükleme hatası: {ex.Message}");
            Profiles = new ObservableCollection<Profile>();
            IsManageMode = false;
        }
    }

    [RelayCommand]
    private void ToggleManageMode()
    {
        IsManageMode = !IsManageMode;
        UpdateShowAddButton();
    }

    private void UpdateShowAddButton()
    {
        // Keep the button visible even if limit is reached (to show upsell),
        // but hide it when in Manage Mode.
        ShowAddButton = !IsManageMode;
    }

    [RelayCommand]
    private async Task AddProfile()
    {
        var profileCount = Profiles.Count;
        if (!_licenseService.IsWithinLimit(ProfilesLimitKey, profileCount))
        {
            await _dialogService.ShowUpsellAsync();
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
        var confirmed = await _dialogService.ShowConfirmationAsync("Profil Sil", 
            $"'{profile.Name}' profilini silmek istediğinize emin misiniz?");
            
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
            await _dialogService.ShowErrorAsync("Hata", "Profil silinirken bir hata oluştu.", ex);
        }
    }

    [RelayCommand]
    private Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
        return Task.CompletedTask;
    }
}


