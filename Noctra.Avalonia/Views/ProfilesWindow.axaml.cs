using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Core.Services;
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
    private readonly IDispatcherService _dispatcherService;
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _mainViewModel;
    private readonly ILocalizationService _localizationService;
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
            ((App)Application.Current!).Services.GetRequiredService<IDispatcherService>(),
            ((App)Application.Current!).Services.GetRequiredService<MainWindow>(),
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<ILocalizationService>())
    {
    }

    public ProfilesWindow(
        ProfilesViewModel viewModel,
        IDbContextFactory<AppDbContext> contextFactory,
        IDialogService dialogService,
        ISettingsService settingsService,
        ISecurityService securityService,
        IProfileService profileService,
        IDispatcherService dispatcherService,
        MainWindow mainWindow,
        MainViewModel mainViewModel,
        ILocalizationService localizationService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _contextFactory = contextFactory;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _securityService = securityService;
        _profileService = profileService;
        _dispatcherService = dispatcherService;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;
        _localizationService = localizationService;

        DataContext = _viewModel;
        _viewModel.RequestClose += ViewModel_RequestClose;
        _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
        _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        _viewModel.OnProfileSelected += ViewModel_OnProfileSelected;
        _viewModel.PinPrompt = VerifyProfilePinAsync;
        Opened += ProfilesWindow_Opened;
    }

    // ── PIN Doğrulama — Merkezi Geçit ─────────────────────────────────
    // Doğrulama ve grant üretimi merkezîdir (ProfilesViewModel +
    // IProfileAccessService); bu yalnızca PIN UI'ını gösterir.
    private async Task<bool> VerifyProfilePinAsync(Profile profile, ProfileAccessPurpose purpose)
    {
        if (string.IsNullOrEmpty(profile.PinHash))
            return true;

        var purposeKey = purpose switch
        {
            ProfileAccessPurpose.Edit or ProfileAccessPurpose.Delete or ProfileAccessPurpose.PinChange
                => "Profiles.Pin.Purpose.Edit",
            _ => "Profiles.Pin.Purpose.Login"
        };

        // Kalıcı kilit kontrolü — profil hâlâ kilitliyse PIN penceresini açma.
        // Kilit ve deneme sayacı veritabanında saklanır; uygulama yeniden
        // başlatılsa bile korunur.
        var state = await _profileService.GetPinVerificationStateAsync(profile.Id);
        if (state.IsLocked)
        {
            var remaining = (int)state.RemainingLockDuration!.Value.TotalSeconds;
            await _dialogService.ShowMessageAsync(
                _localizationService.GetString("PinEntry.Error.LockedTitle"),
                string.Format(_localizationService.GetString("PinEntry.Error.ProfileLockedFormat"), remaining));
            return false;
        }

        var pinVm = new PinEntryViewModel(
            _securityService,
            _dispatcherService,
            profile.PinHash,
            profile.Name,
            profile.Avatar,
            _localizationService.GetString(purposeKey),
            _localizationService,
            state.FailedPinAttempts,
            state.PinLockedUntilUtc);

        var pinWindow = new PinEntryWindow(pinVm);
        // Pencere kapanır kapanmaz (doğru PIN, iptal, X) lockout sayacı iptal
        // edilir — ölü ViewModel artık dispatcher güncellemesi gönderemez.
        pinWindow.Closed += (_, _) => pinVm.Dispose();
        bool? result = null;

        // Kalıcılık — her başarısız deneme veritabanına yazılır; kilit
        // tetiklendiğinde PIN penceresi kilit durumuna geçer.
        pinVm.AttemptFailed += (_, _) =>
        {
            _ = PersistFailureAsync();

            async Task PersistFailureAsync()
            {
                try
                {
                    var newState = await _profileService.RegisterPinFailureAsync(profile.Id);
                    if (newState.IsLocked)
                    {
                        pinVm.ApplyLockout(newState.PinLockedUntilUtc!.Value);
                    }
                }
                catch
                {
                    // DB hatası kilit akışını bozmasın
                }
            }
        };

        pinVm.PinResult += (_, r) =>
        {
            if (r == true)
            {
                _ = _profileService.ResetPinAttemptsAsync(profile.Id);
            }

            result = r;
            pinWindow.Close();
        };

        // Legacy formattan dogrulanan PIN — ayni PIN guncel formatta saklanir.
        pinVm.PinNeedsRehash += (_, pin) =>
        {
            _ = UpgradePinHashAsync(pin);

            async Task UpgradePinHashAsync(string verifiedPin)
            {
                try
                {
                    await _profileService.UpgradePinHashAsync(profile.Id, _securityService.HashPin(verifiedPin));
                }
                catch
                {
                    // Hash yukseltme hatasi giris akisini bozmasin
                }
            }
        };

        await pinWindow.ShowDialog(this);

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
            _localizationService.GetString("Profiles.Pin.Forgot.Title"),
            string.Format(_localizationService.GetString("Profiles.Pin.Forgot.MessageFormat"), profile.Name));

        if (!confirmed) return;

        await _profileService.ScheduleProfileDeletionAsync(profile.Id);
        await _viewModel.RefreshProfilesAsync();

        await _dialogService.ShowMessageAsync(
            _localizationService.GetString("Profiles.Pin.Forgot.ScheduledTitle"),
            string.Format(_localizationService.GetString("Profiles.Pin.Forgot.ScheduledMessageFormat"), profile.Name));
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
            await _dialogService.ShowErrorAsync(_localizationService.GetString("Settings.Error.Title"), _localizationService.GetString("Settings.Error.OpenFailed"), ex);
        }
    }

    private void ToggleManage_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.ToggleManageModeCommand.CanExecute(null) == true)
        {
            _viewModel.ToggleManageModeCommand.Execute(null);
        }
    }

    private void AddProfile_Click(object? sender, RoutedEventArgs e)
    {
        var viewModel = _viewModel ?? DataContext as ProfilesViewModel;
        if (viewModel?.AddProfileCommand.CanExecute(null) == true)
        {
            viewModel.AddProfileCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void SelectProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm || sender is not Control control || control.DataContext is not Profile profile)
        {
            return;
        }

        // Manage modundaysa düzenleme/silme — PIN kontrolü merkezî (ViewModel)
        if (vm.IsManageMode)
        {
            vm.EditProfileCommand.Execute(profile);
            return;
        }

        vm.SelectProfileCommand.Execute(profile);
    }

    private async void ProfilesWindow_Opened(object? sender, EventArgs e)
    {
        try
        {
            {
                await _viewModel.RefreshProfilesAsync();
            }

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
            _mainViewModel.StatusMessage = $"{_localizationService.GetString("Profiles.Error.Loading")}: {ex.Message}";
        }
    }

    private async void ViewModel_OnProfileSelected(Profile profile, ProfileAccessGrant? grant)
    {
        ProfileLoadingWindow? loadingWindow = null;
        try
        {
            Profile? reloadedProfile;
            {
                using var db = await _contextFactory.CreateDbContextAsync();
                reloadedProfile = await db.Profiles
                    .Include(p => p.ProviderAccount)
                    .FirstOrDefaultAsync(p => p.Id == profile.Id);
            }

            if (reloadedProfile == null)
            {
                _mainViewModel.StatusMessage = _localizationService.GetString("Profiles.Error.NotFound");
                return;
            }

            // Create and show loading window
            var loadingVm = ((App)Application.Current!).Services.GetRequiredService<ProfileLoadingViewModel>();
            loadingVm.SetProfile(reloadedProfile);
            loadingVm.StatusMessage = _localizationService.GetString("Profiles.Status.Preparing");
            
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
            var loadTask = _mainViewModel.LoadProfileAsync(reloadedProfile, grant);
            
            {
                await Task.WhenAll(minDelayTask, loadTask);
            }
            
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
                loadingVm.StatusMessage = $"{_localizationService.GetString("Settings.Error.Title")}: {ex.Message}";
                
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

    private void ViewModel_OnProfileEditRequested(Profile profile, ProfileAccessGrant? grant)
    {
        // PIN kontrolü merkezîdir (ViewModel) — bu event yalnızca
        // geçerli bir grant ile tetiklenir; grant düzenleme ekranına taşınır.
        OpenAddProfileWindow(profile, grant);
    }

    private async void OpenAddProfileWindow(Profile? profileToEdit, ProfileAccessGrant? grant = null)
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
                success = await _dialogService.ShowEditProfileAsync(profileToEdit, grant);
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
        _viewModel.PinPrompt = null;
        Opened -= ProfilesWindow_Opened;

        base.OnClosed(e);
    }

    private void ViewModel_RequestClose()
    {
        Close(true);
    }
}
