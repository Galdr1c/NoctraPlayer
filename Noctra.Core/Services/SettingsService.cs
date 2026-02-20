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
    private AppSettings? _settings;
    private bool _isLoaded;
    private readonly object _lock = new();

    public AppSettings Settings
    {
        get
        {
            if (!_isLoaded)
            {
                lock (_lock)
                {
                    if (!_isLoaded)
                    {
                        LoadSync();
                        _isLoaded = true;
                    }
                }
            }
            return _settings!;
        }
    }

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
        
        // Varsayılan değerlerle başlat
        _settings = new AppSettings
        {
            DownloadPath = Path.Combine(appDataPath, "Downloads")
        };
    }
    
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger?.LogInformation("Settings file not found, using defaults");
                _isLoaded = true;
                return;
            }
            
            var json = await File.ReadAllTextAsync(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
            {
                _settings = loaded;
                _isLoaded = true;
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Settings loaded: IsDarkTheme={_settings.IsDarkTheme}");
                _logger?.LogInformation("Settings loaded from {Path}", _settingsPath);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings");
        }
    }
    
    private void LoadSync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _isLoaded = true;
                return;
            }
            
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
            {
                _settings = loaded;
                _isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings synchronously");
        }
    }
    
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
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
        _settings = new AppSettings
        {
            DownloadPath = defaultDownloadPath
        };
        
        _ = SaveAsync();
    }
}


