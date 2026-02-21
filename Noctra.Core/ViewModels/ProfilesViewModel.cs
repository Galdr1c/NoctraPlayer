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
    private readonly IDialogService _dialogService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILicenseService _licenseService;
    private readonly IContentDownloadService _contentDownloadService;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(
        IDbContextFactory<AppDbContext> contextFactory,
        IDialogService dialogService,
        IDispatcherService dispatcherService,
        ILicenseService licenseService,
        IContentDownloadService contentDownloadService)
    {
        _contextFactory = contextFactory;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _licenseService = licenseService;
        _contentDownloadService = contentDownloadService;
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
            using var db = await _contextFactory.CreateDbContextAsync();
            var items = await db.Profiles
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
    private async Task AddProfile()
    {
        using var db = await _contextFactory.CreateDbContextAsync();
        var profileCount = await db.Profiles.CountAsync();
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
        using var db = await _contextFactory.CreateDbContextAsync();
        var dbProfile = await db.Profiles.FindAsync(profile.Id);
        if (dbProfile != null)
        {
            dbProfile.LastUsed = DateTime.UtcNow;
            await db.SaveChangesAsync();
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
            using var db = await _contextFactory.CreateDbContextAsync();
            var profileId = profile.Id;
            var providerAccountId = await db.Profiles
                .Where(p => p.Id == profileId)
                .Select(p => p.ProviderAccountId)
                .FirstOrDefaultAsync();

            if (providerAccountId != 0)
            {
                var hasOtherProfiles = await db.Profiles
                    .AnyAsync(p => p.ProviderAccountId == providerAccountId && p.Id != profileId);

                await _contentDownloadService.DeleteProfileDownloadsAsync(profileId);

                await using var transaction = await db.Database.BeginTransactionAsync();

                await db.WatchHistories
                    .Where(h => h.ProfileId == profileId)
                    .ExecuteDeleteAsync();

                await db.SeriesEpisodeProgresses
                    .Where(p => p.ProfileId == profileId)
                    .ExecuteDeleteAsync();

                await db.Playlists
                    .Where(p => p.ProfileId == profileId)
                    .ExecuteDeleteAsync();

                await db.Profiles
                    .Where(p => p.Id == profileId)
                    .ExecuteDeleteAsync();

                if (!hasOtherProfiles)
                {
                    await db.ProviderAccounts
                        .Where(a => a.Id == providerAccountId)
                        .ExecuteDeleteAsync();
                }

                await transaction.CommitAsync();
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
    private Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
        return Task.CompletedTask;
    }
}


