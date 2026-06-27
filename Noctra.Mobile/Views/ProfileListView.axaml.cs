using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Noctra.Data;
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
    private readonly Dictionary<int, DateTime> _profileLockouts = new();

    public event EventHandler? ProfileLoaded;

    public ProfileListView()
    {
        InitializeComponent();
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
        }

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
            _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
            _viewModel.OnProfileSelected += ViewModel_OnProfileSelected;

            // Refresh profiles when DataContext is set — this replaces the broken
            // AttachedToVisualTree handler which fired before DataContext was assigned.
            _ = _viewModel.RefreshProfilesAsync();
        }
    }

    private async void SelectProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null ||
            sender is not Control { DataContext: Profile profile })
        {
            return;
        }

        if (_viewModel.IsManageMode)
        {
            if (!await VerifyPinIfRequired(profile, "Profiles.Pin.Purpose.Edit"))
            {
                return;
            }

            _viewModel.EditProfileCommand.Execute(profile);
            return;
        }

        if (!await VerifyPinIfRequired(profile, "Profiles.Pin.Purpose.Enter"))
        {
            return;
        }

        if (profile.IsPendingDeletion &&
            Application.Current is App app &&
            app.Services is not null)
        {
            var profileService = app.Services.GetRequiredService<IProfileService>();
            var dialogService = app.Services.GetRequiredService<IDialogService>();
            var localizationService = app.Services.GetRequiredService<ILocalizationService>();

            await profileService.CancelProfileDeletionAsync(profile.Id);
            await _viewModel.RefreshProfilesAsync();
            await dialogService.ShowMessageAsync(
                localizationService.GetString("Profiles.Pin.Recovered.Title"),
                string.Format(localizationService.GetString("Profiles.Pin.Recovered.MessageFormat"), profile.Name));
        }

        _viewModel.SelectProfileCommand.Execute(profile);
    }

    private void ViewModel_OnProfileAddRequested(Profile profile)
    {
        OpenProfileSetup(null);
    }

    private void ViewModel_OnProfileEditRequested(Profile profile)
    {
        OpenProfileSetup(profile);
    }

    private void OpenProfileSetup(Profile? profile)
    {
        if (Application.Current is not App app || app.Services is null)
        {
            return;
        }

        var viewModel = app.Services.GetRequiredService<AddProfileViewModel>();
        if (profile is not null)
        {
            viewModel.InitializeForEdit(profile);
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

    private async Task<bool> VerifyPinIfRequired(Profile profile, string purposeKey)
    {
        if (string.IsNullOrEmpty(profile.PinHash))
        {
            return true;
        }

        if (Application.Current is not App app || app.Services is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        foreach (var expiredKey in _profileLockouts.Where(kvp => kvp.Value <= now).Select(kvp => kvp.Key).ToList())
        {
            _profileLockouts.Remove(expiredKey);
        }

        var dialogService = app.Services.GetRequiredService<IDialogService>();
        var localizationService = app.Services.GetRequiredService<ILocalizationService>();

        if (_profileLockouts.TryGetValue(profile.Id, out var lockoutEnd) && lockoutEnd > now)
        {
            var remaining = (int)(lockoutEnd - now).TotalSeconds;
            await dialogService.ShowMessageAsync(
                localizationService.GetString("PinEntry.Error.LockedTitle"),
                string.Format(localizationService.GetString("PinEntry.Error.ProfileLockedFormat"), remaining));
            return false;
        }

        var completion = new TaskCompletionSource<bool?>();
        var pinViewModel = new PinEntryViewModel(
            app.Services.GetRequiredService<ISecurityService>(),
            app.Services.GetRequiredService<IDispatcherService>(),
            profile.PinHash,
            profile.Name,
            profile.Avatar,
            localizationService.GetString(purposeKey),
            localizationService);

        _activePinEntryViewModel = pinViewModel;
        pinViewModel.LockoutTriggered += PinEntry_LockoutTriggered;
        pinViewModel.PinResult += PinEntry_PinResult;
        PinEntryContent.DataContext = pinViewModel;
        PinEntryHost.IsVisible = true;
        PinEntryContent.Focus();

        var result = await completion.Task;
        pinViewModel.LockoutTriggered -= PinEntry_LockoutTriggered;
        pinViewModel.PinResult -= PinEntry_PinResult;
        ClosePinEntry();

        if (result == null)
        {
            await HandleForgotPin(profile);
            return false;
        }

        return result == true;

        void PinEntry_LockoutTriggered(object? sender, DateTime lockoutUntil)
        {
            _profileLockouts[profile.Id] = lockoutUntil;
        }

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

    private async void ViewModel_OnProfileSelected(Profile profile)
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
            var contextFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await contextFactory.CreateDbContextAsync();
            var reloadedProfile = await db.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            if (reloadedProfile is null)
            {
                mainViewModel.StatusMessage = localizationService.GetString("Profiles.Error.NotFound");
                return;
            }

            loadingViewModel.SetProfile(reloadedProfile);
            loadingViewModel.StatusMessage = localizationService.GetString("Profiles.Status.Preparing");
            ProfileLoadingContent.DataContext = loadingViewModel;
            ProfileLoadingHost.IsVisible = true;

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
                await Task.WhenAll(Task.Delay(800), mainViewModel.LoadProfileAsync(reloadedProfile));
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
}
