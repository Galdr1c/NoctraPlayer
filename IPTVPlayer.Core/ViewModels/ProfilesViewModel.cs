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
    
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = new();

    [ObservableProperty]
    private bool _isManageMode;

    public event Action<Profile>? OnProfileSelected;
    public event Action<Profile>? OnProfileAddRequested;
    public event Action<Profile>? OnProfileEditRequested;

    public ProfilesViewModel(AppDbContext context, IDialogService dialogService)
    {
        _context = context;
        _dialogService = dialogService;
        RefreshProfiles();
    }

    public void RefreshProfiles()
    {
        try 
        {
            var items = _context.Profiles
                .Include(p => p.ProviderAccount) // Load account info
                .OrderByDescending(p => p.LastUsed)
                .ToList();
            
            Profiles = new ObservableCollection<Profile>(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profil yükleme hatası: {ex.Message}");
            Profiles = new ObservableCollection<Profile>();
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
        profile.LastUsed = DateTime.Now;
        await _context.SaveChangesAsync();

        OnProfileSelected?.Invoke(profile);
        RequestClose?.Invoke();
    }
    
    [RelayCommand]
    private async Task DeleteProfile(Profile profile)
    {
        if (profile == null) return;
        
        var confirmed = await _dialogService.ShowConfirmationAsync("Profil Sil", 
            $"'{profile.Name}' profilini silmek istediğinize emin misiniz? (Bağlı hesap silinmez)");
            
        if (confirmed)
        {
            _context.Profiles.Remove(profile);
            await _context.SaveChangesAsync();
            Profiles.Remove(profile);
        }
    }

    [RelayCommand]
    private async Task EditProfile(Profile profile)
    {
        OnProfileEditRequested?.Invoke(profile);
    }

    public event Action? RequestClose;
}
