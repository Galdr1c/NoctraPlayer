using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly AppDbContext _context;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILicenseService _licenseService;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(AppDbContext context, IDialogService dialogService, IDispatcherService dispatcherService, ILicenseService licenseService)
    {
        _context = context;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _licenseService = licenseService;
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        // Use a discarded task to run the async method
        _ = LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        try 
        {
            var items = await _context.Profiles
                .Include(p => p.ProviderAccount) // Load account info
                .OrderByDescending(p => p.LastUsed)
                .ToListAsync();
            
            Profiles = new ObservableCollection<Profile>(items);
            if (!items.Any()) IsManageMode = false;
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
    }

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (!_licenseService.IsWithinLimit(LicenseService.Limits.Profiles, Profiles.Count))
        {
            await _dialogService.ShowMessageAsync(
                "Profil Limiti",
                $"Free sürümde maksimum {_licenseService.GetLimit(LicenseService.Limits.Profiles)} profil oluşturabilirsiniz. Premium'a geçin!");

            var result = await _dialogService.ShowConfirmationAsync(
                "Premium'a Yükselt",
                "Sınırsız profil için Premium satın almak ister misiniz?");

            if (result)
            {
                await _dialogService.ShowUpsellAsync();
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
        var dbProfile = await _context.Profiles.FindAsync(profile.Id);
        if (dbProfile != null)
        {
            dbProfile.LastUsed = DateTime.Now;
            await _context.SaveChangesAsync();
        }

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
            var profileId = profile.Id;

            // 2. Find and Remove Profile & Account
            var dbProfile = await _context.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profileId);

            if (dbProfile != null)
            {
                if (dbProfile.ProviderAccount != null)
                {
                    _context.ProviderAccounts.Remove(dbProfile.ProviderAccount);
                }
                
                _context.Profiles.Remove(dbProfile);
                await _context.SaveChangesAsync();
            }

            // 4. Update UI
            var profileToRemove = Profiles.FirstOrDefault(p => p.Id == profileId);
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
    private async Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
    }
}
