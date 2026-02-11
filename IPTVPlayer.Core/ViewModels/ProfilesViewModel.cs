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
    private readonly AppDbContext _context;
    private readonly IDispatcherService _dispatcherService;
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;
    public event Action? RequestClose;

    public ProfilesViewModel(AppDbContext context, IDialogService dialogService, IDispatcherService dispatcherService)
    {
        _context = context;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
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
            var providerAccountId = await _context.Profiles
                .Where(p => p.Id == profileId)
                .Select(p => p.ProviderAccountId)
                .FirstOrDefaultAsync();

            if (providerAccountId != 0)
            {
                var hasOtherProfiles = await _context.Profiles
                    .AnyAsync(p => p.ProviderAccountId == providerAccountId && p.Id != profileId);

                await using var transaction = await _context.Database.BeginTransactionAsync();

                await _context.WatchHistories
                    .Where(h => h.ProfileId == profileId)
                    .ExecuteDeleteAsync();

                await _context.Playlists
                    .Where(p => p.ProfileId == profileId)
                    .ExecuteDeleteAsync();

                await _context.Profiles
                    .Where(p => p.Id == profileId)
                    .ExecuteDeleteAsync();

                if (!hasOtherProfiles)
                {
                    await _context.ProviderAccounts
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
    private async Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
    }
}
