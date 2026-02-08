using System.Windows;
using IPTVPlayer.ViewModels;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IPTVPlayer.Views;

public partial class ProfilesWindow : Window
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProfilesWindow(ProfilesViewModel viewModel, IServiceScopeFactory scopeFactory)
    {
        InitializeComponent();
        DataContext = viewModel;
        _scopeFactory = scopeFactory;
        
        viewModel.RequestClose += () => Close();
        viewModel.OnProfileAddRequested += ViewModel_OnProfileAddRequested;
        viewModel.OnProfileEditRequested += ViewModel_OnProfileEditRequested;
        viewModel.OnProfileSelected += ViewModel_OnProfileSelected;
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
                mainViewModel.StatusMessage = "Profil bulunamadı.";
                return;
            }
            
            // Load Profile Data in background (UI already visible)
            await mainViewModel.LoadProfileAsync(reloadedProfile);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Profil yüklenirken hata: {ex.Message}");
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
                addProfileWin.Owner = this;
                var result = addProfileWin.ShowDialog();
                
                if (result == true)
                {
                    if (DataContext is ProfilesViewModel vm)
                    {
                        vm.RefreshProfiles();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Pencere açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var settingsVm = scope.ServiceProvider.GetRequiredService<SettingsViewModel>();
            var settingsWindow = new SettingsWindow(settingsVm);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }
    }
    
    // ManageProfilesButton_Click removed as it is handled by Command

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Escape tuşu ile çıkış
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
         base.OnClosed(e);
    }
}
