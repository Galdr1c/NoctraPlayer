using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.Data;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.ViewModels;
using IPTVPlayer.Views;

namespace IPTVPlayer;

/// <summary>
/// App.xaml etkileşim mantığı
/// </summary>
public partial class App : Application
{
    public new static App Current => (App)Application.Current;
    public IServiceProvider Services => _serviceProvider!;
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Veritabanını oluştur/güncelle
        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();
        }

        // Global exception handling
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // Premium kontrolü
        try 
        {
            // Ana pencere yerine Profiller penceresini başlat
            var profilesWindow = _serviceProvider.GetRequiredService<ProfilesWindow>();
            profilesWindow.Show();

            /* Premium check disabled for now - Netflix style flow
            var licenseService = _serviceProvider.GetRequiredService<ILicenseService>();
            if (!licenseService.IsPremium)
            {
                var upsellWindow = _serviceProvider.GetRequiredService<UpsellWindow>();
                
                // Pencerenin üstünde açılması için Owner ayarla
                upsellWindow.Owner = profilesWindow;
                upsellWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                
                var result = upsellWindow.ShowDialog();
                
                if (result != true)
                {
                    Shutdown();
                    return;
                }
            }
            */
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uygulama başlatılırken bir hata oluştu:\n{ex.Message}\n\nDetay:\n{ex.InnerException?.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void LogError(string message, Exception? ex)
    {
        try
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "iptv_crash_log.txt");
            string logContent = $"[{DateTime.Now}] {message}\n{ex?.ToString()}\n--------------------------------------------------\n";
            File.AppendAllText(logPath, logContent);
        }
        catch { /* Logging fail shouldn't crash app */ }
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);
        
        string errorDetail = e.Exception.Message;
        if (e.Exception.InnerException != null) errorDetail += $"\n\nDetay: {e.Exception.InnerException.Message}";

        MessageBox.Show($"Beklenmeyen bir hata oluştu ve uygulama devam edemeyebilir.\n\nHata: {errorDetail}", 
                        "Uygulama Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        
        // Bazı durumlarda Handled=true yapıp devam edebiliriz, ama kritikse e.Handled=false bırakılmalı
        e.Handled = true; 
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogError("CurrentDomain_UnhandledException", ex);
            string errorDetail = ex.Message;
            if (ex.InnerException != null) errorDetail += $"\n\nDetay: {ex.InnerException.Message}";

            MessageBox.Show($"Kritik bir sistem hatası oluştu:\n{errorDetail}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Stop);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Sessizce loglayabiliriz veya kullanıcıya gösterebiliriz
        e.SetObserved();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IPTVPlayer",
            "iptv_v2.db");
        
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // HTTP Client
        services.AddHttpClient<IM3UParser, M3UParser>();
        services.AddHttpClient<IEpgService, EpgService>();

        // Services
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
        services.AddSingleton<VideoPlayerService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddSingleton<IEpgService, EpgService>();
        services.AddSingleton<Services.Interfaces.IAvatarService, Services.Interfaces.AvatarService>();
        
        // UI Services (WPF Implementations)
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IThemeService, WpfThemeService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();
        services.AddTransient<WatermarkViewModel>();

        // Windows
        services.AddTransient<MainWindow>();
        services.AddTransient<UpsellWindow>();
        services.AddTransient<ProfilesWindow>();
        services.AddTransient<AddProfileWindow>();
        services.AddTransient<AvatarPickerWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Video player'ı temizle
        if (_serviceProvider != null)
        {
            var playerService = _serviceProvider.GetService<IVideoPlayerService>();
            playerService?.Dispose();
        }
        
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
