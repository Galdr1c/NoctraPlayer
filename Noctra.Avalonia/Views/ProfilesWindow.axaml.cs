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
            ((App)Application.Current!).Services.GetRequiredService<MainWindow>(),
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>())
    {
    }

    public ProfilesWindow(
        ProfilesViewModel viewModel,
        IDbContextFactory<AppDbContext> contextFactory,
        IDialogService dialogService,
        ISettingsService settingsService,
        MainWindow mainWindow,
        MainViewModel mainViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _contextFactory = contextFactory;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _mainWindow = mainWindow;
        _mainViewModel = mainViewModel;

        DataContext = _viewModel;
        _viewModel.RequestClose += ViewModel_RequestClose;
        _viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
        _viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        _viewModel.OnProfileSelected += ViewModel_OnProfileSelected;
        Opened += ProfilesWindow_Opened;
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

    private void SelectProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm || sender is not Control control || control.DataContext is not Profile profile)
        {
            return;
        }

        vm.SelectProfileCommand.Execute(profile);
    }

    private void DeleteProfile_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm || sender is not Control control || control.DataContext is not Profile profile)
        {
            return;
        }

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
            
            // Start listening to StatusMessage from MainViewModel
            void OnStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(MainViewModel.StatusMessage))
                {
                    loadingVm.StatusMessage = _mainViewModel.StatusMessage;
                }
            }
            _mainViewModel.PropertyChanged += OnStatusChanged;

            // Wait for profile to load in background with a minimum duration
            _mainWindow.DataContext = _mainViewModel;
            
            var minDelayTask = Task.Delay(4500); // 4.5 seconds minimum for premium feel
            var loadTask = _mainViewModel.LoadProfileAsync(reloadedProfile);
            
            await Task.WhenAll(minDelayTask, loadTask);
            
            // Re-check for internal errors from MainViewModel (StatusMessage might contain error)
            // If LoadProfileAsync caught an error, MainViewModel.IsLoading might be false but wait...
            // MainViewModel doesn't have IsError property, but StatusMessage is updated.
            
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
