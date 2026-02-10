using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.Data;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.ViewModels;
using System.Net.Http;
using System.Threading.Tasks;
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

        // Veritabanını oluştur/güncelle ve Temayı Uygula
        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();

            // Kayıtlı temayı uygula
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var themeService = scope.ServiceProvider.GetRequiredService<IThemeService>();
            bool isDark = settingsService.Settings.IsDarkTheme;
            themeService.SetTheme(isDark);
            
            // Veritabanı şema güncellemesi (Migration)
            
            // 1. ProviderAccounts -> ExpirationDate
            try
            {
                var command = "ALTER TABLE ProviderAccounts ADD COLUMN ExpirationDate TEXT;";
                context.Database.ExecuteSqlRaw(command);
            }
            catch { /* Sütun zaten varsa hata verir, yoksay */ }

            // 2. Profiles -> CreatedAt
            try
            {
                var commandProfile = "ALTER TABLE Profiles ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';";
                context.Database.ExecuteSqlRaw(commandProfile);
            }
            catch { /* Sütun zaten varsa hata verir, yoksay */ }
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

            /* Premium check disabled for now - Noctra style flow
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
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            string logContent = $"[{DateTime.Now}] {message}\n{ex?.GetType().Name}: {ex?.Message}\nStack Trace:\n{ex?.StackTrace}\nInner Exception: {ex?.InnerException?.Message}\n--------------------------------------------------\n";
            File.AppendAllText(logPath, logContent);
        }
        catch { /* Logging fail shouldn't crash app */ }
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogError("DispatcherUnhandledException", e.Exception);
        
        // Kritik hatalar - Crash etmeli
        if (IsRecoverableException(e.Exception))
        {
            ShowUserFriendlyError(e.Exception);
            e.Handled = true;
        }
        else
        {
            // Kritik hata - Crash ile birlikte log
            MessageBox.Show(
                "Kritik bir hata oluştu ve uygulama kapatılacak.\n\n" +
                "Hata raporu masaüstüne kaydedildi.",
                "Kritik Hata", 
                MessageBoxButton.OK, 
                MessageBoxImage.Stop);
            
            e.Handled = false; // Crash et
        }
    }

    private bool IsRecoverableException(Exception ex)
    {
        // Kurtarılabilir hatalar
        return ex is HttpRequestException ||
               ex is TimeoutException ||
               ex is OperationCanceledException ||
               ex is InvalidOperationException ||
               ex is FormatException ||
               ex is ArgumentException;
    }

    private void ShowUserFriendlyError(Exception ex)
    {
        string userMessage = ex switch
        {
            HttpRequestException => "İnternet bağlantınızı kontrol edin ve tekrar deneyin.",
            TimeoutException => "İşlem zaman aşımına uğradı. Lütfen tekrar deneyin.",
            DbUpdateException => "Veritabanı güncellenirken hata oluştu. Uygulamayı yeniden başlatın.",
            InvalidOperationException when ex.Message.Contains("playlist") 
                => "Playlist yüklenirken hata oluştu. URL'yi kontrol edin.",
            _ => "Beklenmeyen bir hata oluştu. Lütfen uygulamayı yeniden başlatın."
        };
        
        MessageBox.Show(
            $"{userMessage}\n\nTeknik Detay: {ex.Message}",
            "Hata",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
        LogError("UnobservedTaskException", e.Exception);
        
        // Background task hataları genelde kritik değil ama kullanıcıya bildirilmeli
        // Use Application.Current.Dispatcher since we are in App.xaml.cs
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (Application.Current.MainWindow?.IsVisible == true)
            {
                MessageBox.Show(
                    "Arka planda bir hata oluştu. Bazı özellikler çalışmayabilir.",
                    "Uyarı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        });
        
        e.SetObserved();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra",
            "noctra_v1.db");
        
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"), ServiceLifetime.Scoped);

        // HTTP Client
        services.AddHttpClient<IM3UParser, M3UParser>();
        services.AddHttpClient<IEpgService, EpgService>();
        services.AddHttpClient<IMetadataService, MetadataService>();

        // Services
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddSingleton<IVideoPlayerService, VideoPlayerService>();
        services.AddSingleton<ILicenseService, LicenseService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IWatchHistoryService, WatchHistoryService>();
        services.AddSingleton<Services.Interfaces.IAvatarService, Services.Interfaces.AvatarService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<HoverPreviewService>();
        
        // UI Services (WPF Implementations)
        services.AddSingleton<IDispatcherService, WpfDispatcherService>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IThemeService, WpfThemeService>();

        // ViewModels - Singleton for instant profile switching
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddScoped<ProfilesViewModel>();
        services.AddScoped<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();
        services.AddTransient<WatermarkViewModel>();
        services.AddTransient<GlobalSettingsViewModel>();

        // Windows - Singleton MainWindow for instant loading
        services.AddSingleton<MainWindow>();
        services.AddTransient<UpsellWindow>();
        services.AddTransient<ProfilesWindow>();
        services.AddTransient<AddProfileWindow>();
        services.AddTransient<AvatarPickerWindow>();
        services.AddTransient<AvatarPickerWindow>();
        services.AddTransient<GlobalSettingsWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<EditChannelWindow>();

        services.AddTransient<EditChannelViewModel>();
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
