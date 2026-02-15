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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private readonly MainWindow _mainWindow;
    private readonly MainViewModel _mainViewModel;
    private bool _autoSelectTriggered;
    private bool _isAddProfileWindowOpen;

    public bool DisableAutoSelect { get; set; }

    public ProfilesWindow()
        : this(
            ((App)Application.Current!).Services.GetRequiredService<ProfilesViewModel>(),
            ((App)Application.Current!).Services.GetRequiredService<IServiceScopeFactory>(),
            ((App)Application.Current!).Services.GetRequiredService<ISettingsService>(),
            ((App)Application.Current!).Services.GetRequiredService<MainWindow>(),
            ((App)Application.Current!).Services.GetRequiredService<MainViewModel>())
    {
    }

    public ProfilesWindow(
        ProfilesViewModel viewModel,
        IServiceScopeFactory scopeFactory,
        ISettingsService settingsService,
        MainWindow mainWindow,
        MainViewModel mainViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _scopeFactory = scopeFactory;
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
        using var scope = _scopeFactory.CreateScope();
        var viewModel = scope.ServiceProvider.GetRequiredService<GlobalSettingsViewModel>();
        var window = scope.ServiceProvider.GetRequiredService<GlobalSettingsWindow>();
        window.DataContext = viewModel;
        await window.ShowDialog(this);
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

    private async void ViewModel_OnProfileSelected(Profile profile)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reloadedProfile = await context.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            if (reloadedProfile == null)
            {
                _mainViewModel.StatusMessage = "Profil bulunamadi.";
                return;
            }

            _mainWindow.DataContext = _mainViewModel;
            await _mainViewModel.LoadProfileAsync(reloadedProfile);
            _mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = _mainWindow;
            }

            _mainWindow.Show();
            Close(true);
        }
        catch (Exception ex)
        {
            await ((App)Application.Current!).Services
                .GetRequiredService<IDialogService>()
                .ShowErrorAsync("Hata", "Profil secilirken hata olustu.", ex);
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
        if (_isAddProfileWindowOpen)
        {
            return;
        }

        _isAddProfileWindowOpen = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var addProfileVm = scope.ServiceProvider.GetRequiredService<AddProfileViewModel>();

            if (profileToEdit != null)
            {
                addProfileVm.InitializeForEdit(profileToEdit);
            }

            var addProfileWindow = scope.ServiceProvider.GetRequiredService<AddProfileWindow>();
            addProfileWindow.DataContext = addProfileVm;
            var result = await addProfileWindow.ShowDialog<bool?>(this);
            if (result == true)
            {
                await _viewModel.RefreshProfilesAsync();
            }
        }
        finally
        {
            _isAddProfileWindowOpen = false;
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
