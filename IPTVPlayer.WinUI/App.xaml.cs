using Microsoft.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using IPTVPlayer.Services.Interfaces;
using IPTVPlayer.Services;
using IPTVPlayer.ViewModels;
using IPTVPlayer.Data;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System;

namespace IPTVPlayer.WinUI;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; }
    public new static App Current => (App)Application.Current;
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

        // Core Services
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iptvplayer_v2.db")}"));
        
        services.AddSingleton<IPlaylistService, PlaylistService>();
        services.AddSingleton<IEpgService, EpgService>();
        services.AddSingleton<ILicenseService, Services.StoreEntitlementService>();
        services.AddTransient<IVideoPlayerService, VideoPlayerService>();
        services.AddSingleton<IAvatarService, AvatarService>();
        
        // UI Services (Implementations)
        services.AddSingleton<IDialogService, Services.WinUIDialogService>(); 
        services.AddSingleton<IDispatcherService, Services.WinUIDispatcherService>();
        services.AddSingleton<IThemeService, Services.WinUIThemeService>(); 

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<WatermarkViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AddProfileViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }
}
