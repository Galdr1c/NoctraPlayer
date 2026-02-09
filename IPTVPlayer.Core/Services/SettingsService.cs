using System.Text.Json;
using IPTVPlayer.Models;
using Microsoft.Extensions.Logging;

namespace IPTVPlayer.Services;

/// <summary>
/// JSON dosyasına ayarları kaydetip yükleyen servis
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ILogger<SettingsService>? _logger;
    private readonly string _settingsPath;
    private AppSettings _settings = new();
    
    public AppSettings Settings => _settings;
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
        
        // Varsayılan indirme yolunu ayarla
        _settings.DownloadPath = Path.Combine(appDataPath, "Downloads");
        
        // Başlangıçta yükle (sync)
        LoadSync();
    }
    
    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                _logger?.LogInformation("Settings file not found, using defaults");
                return;
            }
            
            var json = await File.ReadAllTextAsync(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
            {
                _settings = loaded;
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
                return;
            
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
                _settings = loaded;
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
        var defaultDownloadPath = _settings.DownloadPath; // Keep download path
        _settings = new AppSettings
        {
            DownloadPath = defaultDownloadPath
        };
        
        _ = SaveAsync();
    }
}
