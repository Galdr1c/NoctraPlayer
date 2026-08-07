using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Models;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using AddProfileViewModel = Noctra.ViewModels.AddProfileViewModel;
using CoreMainViewModel = Noctra.ViewModels.MainViewModel;

namespace Noctra.Mobile.Views;

public partial class ProfileListView : UserControl
{
    private ProfilesViewModel? _viewModel;
    private AddProfileViewModel? _activeProfileSetupViewModel;
    private PinEntryViewModel? _activePinEntryViewModel;

    public event EventHandler? ProfileLoaded;

    public ProfileListView()
    {
        InitializeComponent();
    }

    public void SetProfilesViewModel(ProfilesViewModel viewModel)
    {
        DataContext = viewModel;
        BindViewModel(viewModel);
    }

    public void ClearProfilesViewModel()
    {
        DataContext = null;
        BindViewModel(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel(DataContext as ProfilesViewModel);
    }

    private void BindViewModel(ProfilesViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.OnProfileAddRequested -= ViewModel_OnProfileAddRequested;
            _viewModel.OnProfileEditRequested -= ViewModel_OnProfileEditRequested;
            _viewModel.OnProfileSelected -= ViewModel_OnProfileSelected;
            _viewModel.PinPrompt = null;
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
            _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
            _viewModel.OnProfileSelected += ViewModel_OnProfileSelected;

            // PIN doğrulama UI'ı — doğrulama ve grant üretimi merkezîdir
            // (ProfilesViewModel + IProfileAccessService); bu view yalnızca
            // PIN ekranını gösterir.
            _viewModel.PinPrompt = VerifyProfilePinAsync;

            // Refresh profiles when DataContext is set — this replaces the broken
            // AttachedToVisualTree handler which fired before DataContext was assigned.
            _ = _viewModel.RefreshProfilesAsync();
        }
    }

    private void SelectProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null ||
            sender is not Control { DataContext: Profile profile })
        {
            return;
        }

        if (_viewModel.IsManageMode)
        {
            _viewModel.EditProfileCommand.Execute(profile);
            return;
        }

        _viewModel.SelectProfileCommand.Execute(profile);
    }

    /// <summary>
    /// Üst başlıktaki kalem (✏) veya "Bitti" butonuna tıklanınca
    /// yönet modunu açar / kapar.
    /// </summary>
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

    private void ViewModel_OnProfileAddRequested(Profile profile)
    {
        OpenProfileSetup(null, null);
    }

    private void ViewModel_OnProfileEditRequested(Profile profile, ProfileAccessGrant? grant)
    {
        OpenProfileSetup(profile, grant);
    }

    private void OpenProfileSetup(Profile? profile, ProfileAccessGrant? grant)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var viewModel = app.Services.GetRequiredService<AddProfileViewModel>();
        if (profile is not null)
        {
            viewModel.InitializeForEdit(profile);
            viewModel.AccessGrant = grant;
        }

        _activeProfileSetupViewModel = viewModel;
        _activeProfileSetupViewModel.RequestClose += ProfileSetup_RequestClose;

        ProfileSetupContent.Content = new ProfileSetupView
        {
            DataContext = viewModel
        };
        ProfileSetupHost.IsVisible = true;
    }

    private async void ProfileSetup_RequestClose(object? sender, EventArgs e)
    {
        if (_activeProfileSetupViewModel is not null)
        {
            _activeProfileSetupViewModel.RequestClose -= ProfileSetup_RequestClose;
            _activeProfileSetupViewModel = null;
        }

        ProfileSetupHost.IsVisible = false;
        ProfileSetupContent.Content = null;

        if (_viewModel is not null)
        {
            await _viewModel.RefreshProfilesAsync();
        }
    }

    private async Task<bool> VerifyProfilePinAsync(Profile profile, ProfileAccessPurpose purpose)
    {
        if (string.IsNullOrEmpty(profile.PinHash))
        {
            return true;
        }

        if (Application.Current is not App app || app.Services is null)
        {
            return false;
        }

        var dialogService = app.Services.GetRequiredService<IDialogService>();
        var localizationService = app.Services.GetRequiredService<ILocalizationService>();
        var profileService = app.Services.GetRequiredService<IProfileService>();

        var purposeKey = purpose switch
        {
            // Silme, kendi amaca özel çevirisini kullanır; düzenleme ve PIN
            // değiştirme "düzenleme" metnini paylaşır. (Profiles.Pin.Purpose.Delete)
            ProfileAccessPurpose.Delete => "Profiles.Pin.Purpose.Delete",
            ProfileAccessPurpose.Edit or ProfileAccessPurpose.PinChange
                => "Profiles.Pin.Purpose.Edit",
            _ => "Profiles.Pin.Purpose.Enter"
        };

        // Kalıcı kilit kontrolü — profil hâlâ kilitliyse PIN penceresini açma
        var state = await profileService.GetPinVerificationStateAsync(profile.Id);
        if (state.IsLocked)
        {
            var remaining = (int)state.RemainingLockDuration!.Value.TotalSeconds;
            await dialogService.ShowMessageAsync(
                localizationService.GetString("PinEntry.Error.LockedTitle"),
                string.Format(localizationService.GetString("PinEntry.Error.ProfileLockedFormat"), remaining));
            return false;
        }

        var completion = new TaskCompletionSource<bool?>();
        // Doğrulama + sayaç/kilit güncellemesi tek atomik servis çağrısında
        // yapılır (VerifyAttemptAsync) — ViewModel keypad'i doğrulama boyunca
        // kilitler, UI ayrıca persist etmez. Böylece art arda girilen hatalı
        // PIN'ler veritabanındaki sayacı kaybettiremez. Verifier çağırandan
        // geçirilmez; servis güncel PinHash'i DB'den okur.
        var pinViewModel = new PinEntryViewModel(
            app.Services.GetRequiredService<IProfileService>(),
            app.Services.GetRequiredService<IDispatcherService>(),
            profile.Id,
            profile.Name,
            profile.Avatar,
            localizationService.GetString(purposeKey),
            localizationService,
            state.FailedPinAttempts,
            state.PinLockedUntilUtc);

        _activePinEntryViewModel = pinViewModel;
        pinViewModel.PinResult += PinEntry_PinResult;
        PinEntryContent.DataContext = pinViewModel;
        PinEntryHost.IsVisible = true;
        PinEntryContent.Focus();

        var result = await completion.Task;
        pinViewModel.PinResult -= PinEntry_PinResult;
        // Akış bitti (doğru PIN, iptal veya "şifremi unuttum") — devam eden
        // lockout sayacını iptal et; ölü ViewModel artık UI güncellemesi yapamaz.
        pinViewModel.Dispose();
        ClosePinEntry();

        if (result == null)
        {
            await HandleForgotPin(profile);
            return false;
        }

        return result == true;

        void PinEntry_PinResult(object? sender, bool? value)
        {
            completion.TrySetResult(value);
        }

    }

    private void ClosePinEntry()
    {
        if (_activePinEntryViewModel is not null)
        {
            _activePinEntryViewModel = null;
        }

        PinEntryHost.IsVisible = false;
        PinEntryContent.DataContext = null;
    }

    private async Task HandleForgotPin(Profile profile)
    {
        if (Application.Current is not App app || app.Services is null || _viewModel is null)
        {
            return;
        }

        var dialogService = app.Services.GetRequiredService<IDialogService>();
        var localizationService = app.Services.GetRequiredService<ILocalizationService>();
        var profileService = app.Services.GetRequiredService<IProfileService>();

        var confirmed = await dialogService.ShowConfirmationAsync(
            localizationService.GetString("Profiles.Pin.Forgot.Title"),
            string.Format(localizationService.GetString("Profiles.Pin.Forgot.MessageFormat"), profile.Name));

        if (!confirmed)
        {
            return;
        }

        await profileService.ScheduleProfileDeletionAsync(profile.Id);
        await _viewModel.RefreshProfilesAsync();

        await dialogService.ShowMessageAsync(
            localizationService.GetString("Profiles.Pin.Forgot.ScheduledTitle"),
            string.Format(localizationService.GetString("Profiles.Pin.Forgot.ScheduledMessageFormat"), profile.Name));
    }

    private async void ViewModel_OnProfileSelected(Profile profile, ProfileAccessGrant? grant)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var localizationService = app.Services.GetRequiredService<ILocalizationService>();
        var loadingViewModel = app.Services.GetRequiredService<ProfileLoadingViewModel>();
        var mainViewModel = app.Services.GetRequiredService<CoreMainViewModel>();
        var loadedSuccessfully = false;

        try
        {
            loadingViewModel.SetProfile(profile);
            loadingViewModel.StatusMessage = localizationService.GetString("Profiles.Status.Preparing");
            ProfileLoadingContent.DataContext = loadingViewModel;
            ProfileLoadingHost.IsVisible = true;

            // Queue the continuation below render priority. This guarantees that the
            // profile shell becomes visible before Android SQLite starts cached-content
            // discovery, whose async provider may still perform synchronous work.
            await Dispatcher.UIThread.InvokeAsync(
                static () => { },
                DispatcherPriority.Background);

            void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(CoreMainViewModel.StatusMessage))
                {
                    loadingViewModel.StatusMessage = mainViewModel.StatusMessage;
                }
                else if (e.PropertyName == nameof(CoreMainViewModel.LoadingWarningMessage))
                {
                    loadingViewModel.LoadingWarningMessage = mainViewModel.LoadingWarningMessage;
                }
            }

            mainViewModel.PropertyChanged += OnStatusChanged;
            try
            {
                await Task.WhenAll(Task.Delay(800), mainViewModel.LoadProfileAsync(profile, grant));
                loadedSuccessfully = true;
            }
            finally
            {
                mainViewModel.PropertyChanged -= OnStatusChanged;
            }
        }
        catch (Exception ex)
        {
            loadingViewModel.IsError = true;
            loadingViewModel.StatusMessage = $"{localizationService.GetString("Settings.Error.Title")}: {ex.Message}";
            await Task.Delay(2500);
        }
        finally
        {
            ProfileLoadingHost.IsVisible = false;
            ProfileLoadingContent.DataContext = null;
        }

        if (loadedSuccessfully)
        {
            ProfileLoaded?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryHandleBack()
    {
        // 1. PIN girişini kapat
        if (PinEntryHost.IsVisible && _activePinEntryViewModel is not null)
        {
            _activePinEntryViewModel.CancelCommand.Execute(null);
            return true;
        }

        // 2. Profil kurulumunu (veya onun altındaki avatar seçiciyi) kapat
        if (ProfileSetupHost.IsVisible && ProfileSetupContent.Content is ProfileSetupView setupView)
        {
            if (setupView.TryHandleBack())
            {
                return true;
            }

            if (_activeProfileSetupViewModel is not null)
            {
                _activeProfileSetupViewModel.CancelCommand.Execute(null);
                return true;
            }
        }

        // 3. Yönetim modundaysak kapat
        if (_viewModel is { IsManageMode: true })
        {
            _viewModel.ToggleManageModeCommand.Execute(null);
            return true;
        }

        return false;
    }
}
