using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.Services;
using IPTVPlayer.ViewModels; // Corrected namespace
using IPTVPlayer.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System;
using IPTVPlayer.WinUI.Services; // For FFmpegPlayerService

namespace IPTVPlayer.WinUI;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; }
    public static App Instance => (App)Application.Current;
    public Window MainWindow { get; set; }

    public App()
    {
        this.InitializeComponent();
        Services = ConfigureServices();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize DB
        try {
            var context = Services.GetRequiredService<AppDbContext>();
            context.Database.EnsureCreated();
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"DB Init Error: {ex}");
        }

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Activate();
    }

    private IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Database
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={GetDbPath()}"), 
            ServiceLifetime.Transient);
        
        // Core Services
        services.AddTransient<IPlaylistService, PlaylistService>();
        services.AddSingleton<ILicenseService, StoreEntitlementService>();
        services.AddHttpClient<IM3UParser, M3UParser>();
        services.AddHttpClient<IEpgService, EpgService>();
        
        // FFmpeg Player Service (Singleton!)
        services.AddSingleton<FFmpegPlayerService>();
        services.AddSingleton<IVideoPlayerService>(sp => 
            sp.GetRequiredService<FFmpegPlayerService>());
        
        services.AddSingleton<IAvatarService, AvatarService>();
        
        // UI Services (Implementations)
        services.AddSingleton<IDialogService, WinUIDialogService>(); 
        services.AddSingleton<IDispatcherService, WinUIDispatcherService>();
        services.AddSingleton<IThemeService, WinUIThemeService>(); 

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<WatermarkViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AddProfileViewModel>();
        services.AddTransient<AvatarPickerViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private string GetDbPath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IPTVPlayer");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "iptv_v2.db");
    }
}
