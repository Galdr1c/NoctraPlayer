using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using IPTVPlayer.Data;
using IPTVPlayer.Models;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDispatcherService _dispatcherService;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(IDbContextFactory<AppDbContext> contextFactory, IDialogService dialogService, IDispatcherService dispatcherService)
    {
        _contextFactory = contextFactory;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        Task.Run(async () => 
        {
            try 
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var items = await context.Profiles
                    .Include(p => p.ProviderAccount) // Load account info
                    .OrderByDescending(p => p.LastUsed)
                    .ToListAsync();
                
                _dispatcherService.Invoke(() => 
                {
                    Profiles = new ObservableCollection<Profile>(items);
                    if (!items.Any()) IsManageMode = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Profil yükleme hatası: {ex.Message}");
                _dispatcherService.Invoke(() => 
                {
                    Profiles = new ObservableCollection<Profile>();
                    IsManageMode = false;
                });
            }
        });
    }

    [RelayCommand]
    private void ToggleManageMode()
    {
        IsManageMode = !IsManageMode;
    }

    [RelayCommand]
    private void AddProfile()
    {
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
        using (var context = await _contextFactory.CreateDbContextAsync())
        {
            var dbProfile = await context.Profiles.FindAsync(profile.Id);
            if (dbProfile != null)
            {
                dbProfile.LastUsed = DateTime.Now;
                await context.SaveChangesAsync();
            }
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
            using var context = await _contextFactory.CreateDbContextAsync();
            var dbProfile = await context.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profileId);

            if (dbProfile != null)
            {
                if (dbProfile.ProviderAccount != null)
                {
                    context.ProviderAccounts.Remove(dbProfile.ProviderAccount);
                }
                
                context.Profiles.Remove(dbProfile);
                await context.SaveChangesAsync();
            }

            // 4. Update UI (Thread-safe)
            var profileToRemove = Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profileToRemove != null)
            {
                _dispatcherService.Invoke(() => 
                {
                    Profiles.Remove(profileToRemove);
                    if (!Profiles.Any()) IsManageMode = false;
                });
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
