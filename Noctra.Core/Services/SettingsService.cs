using System.Text.Json;
using Noctra.Models;
using Microsoft.Extensions.Logging;

namespace Noctra.Services;

/// <summary>
/// JSON dosyasına ayarları kaydetip yükleyen servis
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService>? _logger;
    private readonly string _settingsPath;
    private Lazy<AppSettings> _settings;

    public AppSettings Settings => _settings.Value;

    public event Action? SettingsChanged;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public SettingsService(ILogger<SettingsService>? logger = null)
    {
        _logger = logger;
        
        // Settings dosyası LocalAppData/Noctra/settings.json
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra");
        
        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");

        _settings = new Lazy<AppSettings>(LoadInternalSync);
    }
    
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger?.LogInformation("Settings file not found, using defaults");
                _settings = new Lazy<AppSettings>(CreateDefaultSettings);
                return;
            }
            
            var json = await File.ReadAllTextAsync(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
            {
                _settings = new Lazy<AppSettings>(() => loaded);
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Settings loaded: IsDarkTheme={Settings.IsDarkTheme}");
                _logger?.LogInformation("Settings loaded from {Path}", _settingsPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings");
        }
    }
    
    public void LoadSync()
    {
        _ = _settings.Value;
    }

    private AppSettings LoadInternalSync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return CreateDefaultSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (loaded != null)
            {
                return loaded;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings synchronously");
        }

        return CreateDefaultSettings();
    }

    private AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            DownloadPath = Path.Combine(Path.GetDirectoryName(_settingsPath)!, "Downloads")
        };
    }
    
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(_settingsPath, json);
            
            _logger?.LogInformation("Settings saved to {Path}", _settingsPath);
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }
    
    public void ResetToDefaults()
    {
        var defaultDownloadPath = Settings.DownloadPath; // Keep download path
        var newSettings = new AppSettings
        {
            DownloadPath = defaultDownloadPath
        };

        // Replace lazy instance with a pre-initialized one
        _settings = new Lazy<AppSettings>(() => newSettings);

        _ = SaveAsync();
    }
}


