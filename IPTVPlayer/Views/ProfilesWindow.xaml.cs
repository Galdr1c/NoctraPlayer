using System.Windows;
using System.Windows.Input;
using IPTVPlayer.ViewModels;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Views;

public partial class ProfilesWindow : Window
{
    private readonly IServiceScopeFactory _scopeFactory;
    private bool _autoSelectTriggered;
    private bool _isAddProfileWindowOpen;
    public bool DisableAutoSelect { get; set; }

    public ProfilesWindow(ProfilesViewModel viewModel, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        DataContext = viewModel;
        _scopeFactory = scopeFactory;
        
        viewModel.RequestClose += () => Close();
        viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
        viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        viewModel.OnProfileSelected += ViewModel_OnProfileSelected;

        // Window sÃ¼rÃ¼kleme
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        Loaded += ProfilesWindow_Loaded;
    }

    private async void ProfilesWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesViewModel vm)
        {
            return;
        }

        await vm.RefreshProfilesAsync();

        try
        {
            var settingsService = App.Current.Services.GetRequiredService<ISettingsService>();
            if (DisableAutoSelect || !settingsService.Settings.AutoSelectLastProfile || _autoSelectTriggered)
            {
                return;
            }

            var lastProfile = vm.Profiles.OrderByDescending(p => p.LastUsed).FirstOrDefault();
            if (lastProfile == null)
            {
                return;
            }

            _autoSelectTriggered = true;
            await Task.Delay(50);
            vm.SelectProfileCommand.Execute(lastProfile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto profile select failed: {ex.Message}");
        }
    }

    private async void ViewModel_OnProfileSelected(IPTVPlayer.Models.Profile profile)
    {
        try
        {
            // Get singleton window/viewmodel FIRST
            var mainWindow = App.Current.Services.GetRequiredService<MainWindow>();
            var mainViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
            
            // INSTANT: Show MainWindow immediately (empty/loading state)
            mainWindow.Show();
            Close();
            
            // THEN reload profile data asynchronously in background
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<IPTVPlayer.Data.AppDbContext>();
            
            var reloadedProfile = await context.Profiles
                .Include(p => p.ProviderAccount)
                .FirstOrDefaultAsync(p => p.Id == profile.Id);

            if (reloadedProfile == null)
            {
                mainViewModel.StatusMessage = "Profil bulunamadÄ±.";
                return;
            }
            
            // Load Profile Data in background (UI already visible)
            await mainViewModel.LoadProfileAsync(reloadedProfile);
        }
        catch (Exception ex)
        {
            var msg = $"CRASH IN PROFILE SELECTION: {ex.GetType().Name} - {ex.Message}\n{ex.StackTrace}";
            System.Diagnostics.Debug.WriteLine(msg);
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt"), $"[{DateTime.Now}] {msg}\n\n"); } catch { }
            MessageBox.Show($"Kritik Hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ViewModel_OnProfileEditRequested(IPTVPlayer.Models.Profile profile)
    {
        OpenAddProfileWindow(profile);
    }

    private void ViewModel_OnProfileAddRequested(IPTVPlayer.Models.Profile? obj)
    {
        OpenAddProfileWindow(null);
    }

    private void OpenAddProfileWindow(IPTVPlayer.Models.Profile? profileToEdit)
    {
        if (_isAddProfileWindowOpen)
        {
            return;
        }

        _isAddProfileWindowOpen = true;
        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var licenseService = scope.ServiceProvider.GetRequiredService<ILicenseService>();
                var profilesViewModel = DataContext as ProfilesViewModel;

                // Check profile limit (only for new profiles)
                if (profileToEdit == null && profilesViewModel != null)
                {
                    int currentProfileCount = profilesViewModel.Profiles.Count;
                    if (!licenseService.IsWithinLimit(LicenseService.Limits.Profiles, currentProfileCount))
                    {
                        var upsellWindow = new UpsellWindow(licenseService);
                        upsellWindow.Owner = this;
                        upsellWindow.ShowDialog();
                        return;
                    }
                }

                var addProfileVm = scope.ServiceProvider.GetRequiredService<AddProfileViewModel>();

                if (profileToEdit != null)
                {
                    addProfileVm.InitializeForEdit(profileToEdit);
                }

                var addProfileWin = new AddProfileWindow(addProfileVm, scope.ServiceProvider);
                if (IsLoaded && !IsClosed())
                {
                    addProfileWin.Owner = this;
                }

                var result = addProfileWin.ShowDialog();
                if (result == true && DataContext is ProfilesViewModel vm)
                {
                    vm.RefreshProfiles();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Pencere acilirken hata olustu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isAddProfileWindowOpen = false;
        }
    }
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var globalSettingsVm = scope.ServiceProvider.GetRequiredService<GlobalSettingsViewModel>();
            var globalSettingsWindow = new GlobalSettingsWindow(globalSettingsVm);
            if (IsLoaded && !IsClosed())
            {
                globalSettingsWindow.Owner = this;
            }
            globalSettingsWindow.ShowDialog();
        }
    }
    
    // ManageProfilesButton_Click removed as it is handled by Command

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Escape tuÅŸu ile Ã§Ä±kÄ±ÅŸ
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Application.Current.Shutdown();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
         if (DataContext is ProfilesViewModel vm)
         {
             vm.RequestClose -= Close;
             vm.OnProfileAddRequested -= ViewModel_OnProfileAddRequested;
             vm.OnProfileSelected -= ViewModel_OnProfileSelected;
         }
         Loaded -= ProfilesWindow_Loaded;
         base.OnClosed(e);
    }

    private bool IsClosed()
    {
        return !IsVisible && PresentationSource.FromVisual(this) == null;
    }
}
