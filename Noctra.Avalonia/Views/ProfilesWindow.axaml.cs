using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

public partial class ProfilesWindow : Window
{
    private readonly ProfilesViewModel _viewModel;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private readonly ISecurityService _securityService;
    private readonly IProfileService _profileService;
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _mainViewModel;
    private bool _autoSelectTriggered;
    private int _isAddProfileWindowOpen; // 0 = closed, 1 = open

    public bool DisableAutoSelect { get; set; }

    public ProfilesWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<ProfilesViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IDbContextFactory<AppDbContext>>(),
            ((App)Application.Current!).Services.GetRequiredService<IDialogService>(),
            ((App)Application.Current!).Services.GetRequiredService<ISettingsService>(),
            ((App)Application.Current!).Services.GetRequiredService<ISecurityService>(),
            ((App)Application.Current!).Services.GetRequiredService<IProfileService>(),
            ((App)Application.Current!).Services.GetRequiredService<MainWindow>(),
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>())
    {
    }

    public ProfilesWindow(
        ProfilesViewModel viewModel,
        IDbContextFactory<AppDbContext> contextFactory,
        IDialogService dialogService,
        ISettingsService settingsService,
        ISecurityService securityService,
        IProfileService profileService,
        MainWindow mainWindow,
        MainViewModel mainViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _contextFactory = contextFactory;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _securityService = securityService;
        _profileService = profileService;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;

        DataContext = _viewModel;
        _viewModel.RequestClose += ViewModel_RequestClose;
        _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
        _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        _viewModel.OnProfileSelected += ViewModel_OnProfileSelected;
        Opened += ProfilesWindow_Opened;
    }

    // ── PIN Doğrulama — Merkezi Geçit ─────────────────────────────────
    /// <summary>
    /// PIN varsa doğrulama penceresi açar.
    /// true = geçebilir, false = reddedildi/iptal/unuttum
    /// </summary>
    private async Task<bool> VerifyPinIfRequired(Profile profile, string purpose = "giriş")
    {
        if (string.IsNullOrEmpty(profile.PinHash))
            return true; // PIN yok, direkt geç

        var pinVm = new PinEntryViewModel(
            _securityService,
            profile.PinHash,
            profile.Name,
            profile.Avatar,
            purpose);

        var pinWindow = new PinEntryWindow(pinVm);
        bool? result = null;

        pinVm.PinResult += (_, r) =>
        {
            result = r;
            pinWindow.Close();
        };

        await pinWindow.ShowDialog(this);

        // null = "Şifremi unuttum" tıklandı
        if (result == null)
        {
            await HandleForgotPin(profile);
            return false;
        }

        return result == true;
    }

    // ── PIN Unutuldu — 3 Günlük Silme Geri Sayımı ─────────────────────
    private async Task HandleForgotPin(Profile profile)
    {
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "PIN'inizi mi Unuttunuz?",
            $"'{profile.Name}' profiline erişmek için PIN gerekiyor.\n\n" +
            "PIN kurtarma seçeneği yoktur.\n\n" +
            "\"Evet\" seçeneği ile profil 3 gün içinde kalıcı olarak silinir. " +
            "Bu süre içinde PIN'inizi hatırlayıp profile girerseniz silme işlemi iptal edilir.\n\n" +
            "Silme başlatılsın mı?");

        if (!confirmed) return;

        await _profileService.ScheduleProfileDeletionAsync(profile.Id);
        await _viewModel.RefreshProfilesAsync();

        await _dialogService.ShowMessageAsync(
            "Silme Zamanlandı",
            $"'{profile.Name}' profili 3 gün içinde silinecek.\n\n" +
            "Bu süre içinde PIN'inizi hatırlayıp profile girerseniz silme işlemi otomatik iptal edilir.");
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Close();
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _dialogService.ShowGlobalSettingsAsync();
        }
        catch (Exception ex)
        {
            await _dialogService.ShowErrorAsync("Hata", "Ayarlar penceresi açılamadı.", ex);
        }
    }

    private async void SelectProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm || sender is not Control control || control.DataContext is not Profile profile)
        {
            return;
        }

        // Manage modundaysa düzenleme/silme — PIN kontrolü orada yapılır
        if (vm.IsManageMode)
        {
            // PIN kontrolü — düzenleme için
            if (!await VerifyPinIfRequired(profile, "bu profili düzenlemek"))
                return;

            vm.EditProfileCommand.Execute(profile);
            return;
        }

        // Normal mod — giriş: PIN kontrolü
        if (!await VerifyPinIfRequired(profile, "bu profile girmek"))
            return;

        // PIN doğru girildi — eğer geri sayım aktifse iptal et
        if (profile.IsPendingDeletion)
        {
            await _profileService.CancelProfileDeletionAsync(profile.Id);
            await _viewModel.RefreshProfilesAsync();

            await _dialogService.ShowMessageAsync(
                "Profil Kurtarıldı!",
                $"'{profile.Name}' profili silme işlemi iptal edildi. Profil güvende.");
        }

        vm.SelectProfileCommand.Execute(profile);
    }

    private async void DeleteProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm || sender is not Control control || control.DataContext is not Profile profile)
        {
            return;
        }

        // PIN kontrolü — silme için
        if (!await VerifyPinIfRequired(profile, "bu profili silmek"))
            return;

        vm.DeleteProfileCommand.Execute(profile);
    }

    private async void ProfilesWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.RefreshProfilesAsync();

            if (DisableAutoSelect || !_settingsService.Settings.AutoSelectLastProfile || _autoSelectTriggered)
            {
                return;
            }

            var lastProfile = _viewModel.Profiles.OrderByDescending(p => p.LastUsed).FirstOrDefault();
            if (lastProfile == null)
            {
                return;
            }

            // Auto-select: PIN korumalı profilleri otomatik seçme
            if (!string.IsNullOrEmpty(lastProfile.PinHash))
            {
                return;
            }

            _autoSelectTriggered = true;
            await Task.Delay(50);
            _viewModel.SelectProfileCommand.Execute(lastProfile);
        }
        catch (Exception ex)
        {
            _mainViewModel.StatusMessage = $"Profil yükleme hatası: {ex.Message}";
        }
    }

    private async void ViewModel_OnProfileSelected(Profile profile)
    {
        ProfileLoadingWindow? loadingWindow = null;
        try
        {
            using var db = await _contextFactory.CreateDbContextAsync();
            var reloadedProfile = await db.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            if (reloadedProfile == null)
            {
                _mainViewModel.StatusMessage = "Profil bulunamadi.";
                return;
            }

            // Create and show loading window
            var loadingVm = ((App)Application.Current!).Services.GetRequiredService<ProfileLoadingViewModel>();
            loadingVm.SetProfile(reloadedProfile);
            loadingVm.StatusMessage = "Profil verileri hazırlanıyor...";
            
            loadingWindow = new ProfileLoadingWindow(loadingVm);
            loadingWindow.Show();
            
            // Give UI thread a tiny breather to show and render the window
            await Task.Delay(100);
            
            // Start listening to StatusMessage and LoadingWarningMessage from MainViewModel
            void OnStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                {
                    loadingVm.StatusMessage = _mainViewModel.StatusMessage;
                }
                else if (e.PropertyName == nameof(MainViewModel.LoadingWarningMessage))
                {
                    loadingVm.LoadingWarningMessage = _mainViewModel.LoadingWarningMessage;
                }
            }
            _mainViewModel.PropertyChanged += OnStatusChanged;

            // Wait for profile to load in background with a minimum duration
            _mainWindow.DataContext = _mainViewModel;
            
            var minDelayTask = Task.Delay(1500); // 1.5 seconds minimum for premium feel
            var loadTask = _mainViewModel.LoadProfileAsync(reloadedProfile);
            
            await Task.WhenAll(minDelayTask, loadTask);
            
            // Clean up status listener
            _mainViewModel.PropertyChanged -= OnStatusChanged;

            _mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = _mainWindow;
            }

            _mainWindow.Show();
            loadingWindow.Close();
            Close(true);
        }
        catch (Exception ex)
        {
            if (loadingWindow != null && loadingWindow.DataContext is ProfileLoadingViewModel loadingVm)
            {
                loadingVm.IsError = true;
                loadingVm.StatusMessage = $"Hata: {ex.Message}";
                
                // Show the error in red for a bit before returning
                await Task.Delay(3000);
            }
            
            loadingWindow?.Close();
            // We don't close the current ProfilesWindow so the user can try again or pick another profile.
        }
    }

    private void ViewModel_OnProfileAddRequested(Profile? profile)
    {
        OpenAddProfileWindow(null);
    }

    private void ViewModel_OnProfileEditRequested(Profile profile)
    {
        // PIN kontrolü düzenleme için — SelectProfile_Click'te zaten yapıldı
        // ama direkt event üzerinden gelen çağrılar için de kontrol
        OpenAddProfileWindow(profile);
    }

    private async void OpenAddProfileWindow(Profile? profileToEdit)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _isAddProfileWindowOpen, 1, 0) != 0)
        {
            return;
        }

        try
        {
            bool success;
            if (profileToEdit != null)
            {
                success = await _dialogService.ShowEditProfileAsync(profileToEdit);
            }
            else
            {
                success = await _dialogService.ShowAddProfileAsync();
            }

            if (success)
            {
                await _viewModel.RefreshProfilesAsync();
            }
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _isAddProfileWindowOpen, 0);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= ViewModel_RequestClose;
        _viewModel.OnProfileAddRequested -= ViewModel_OnProfileAddRequested;
        _viewModel.OnProfileEditRequested -= ViewModel_OnProfileEditRequested;
        _viewModel.OnProfileSelected -= ViewModel_OnProfileSelected;
        Opened -= ProfilesWindow_Opened;

        base.OnClosed(e);
    }

    private void ViewModel_RequestClose()
    {
        Close(true);
    }
}
