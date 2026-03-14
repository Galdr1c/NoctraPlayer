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
    private readonly string _basePath;
    private AppSettings _currentSettings;

    public AppSettings Settings => _currentSettings;

    public event Action? SettingsChanged;
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    
    public SettingsService(ILogger<SettingsService>? logger = null)
    {
        _logger = logger;
        
        _basePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Noctra");
        
        Directory.CreateDirectory(_basePath);
        
        // Initial state
        _currentSettings = LoadInternalSync(0);
    }

    private string GetSettingsPath(int profileId)
    {
        if (profileId == 0)
            return Path.Combine(_basePath, "settings.json");
        
        return Path.Combine(_basePath, $"settings_profile_{profileId}.json");
    }
    
    public async Task LoadAsync()
    {
        await LoadProfileSettingsAsync(0);
    }

    public async Task LoadProfileSettingsAsync(int profileId)
    {
        try
        {
            var path = GetSettingsPath(profileId);
            if (!File.Exists(path))
            {
                _logger?.LogInformation("Settings file not found for profile {Id}, using defaults", profileId);
                _currentSettings = CreateDefaultSettings(profileId);
                SettingsChanged?.Invoke();
                return;
            }
            
            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            
            if (loaded != null)
            {
                loaded.ProfileId = profileId; // Ensure correct ID
                _currentSettings = loaded;
                System.Diagnostics.Debug.WriteLine($"[SettingsService] Settings loaded for profile {profileId}: IsDarkTheme={Settings.IsDarkTheme}");
                _logger?.LogInformation("Settings loaded from {Path}", path);
                SettingsChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings for profile {Id}", profileId);
        }
    }

    public async Task<AppSettings?> PeekProfileSettingsAsync(int profileId)
    {
        try
        {
            var path = GetSettingsPath(profileId);
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded != null) loaded.ProfileId = profileId;
            return loaded;
        }
        catch
        {
            return null;
        }
    }
    
    public void LoadSync()
    {
        _currentSettings = LoadInternalSync(0);
    }

    private AppSettings LoadInternalSync(int profileId)
    {
        try
        {
            var path = GetSettingsPath(profileId);
            if (!File.Exists(path))
            {
                return CreateDefaultSettings(profileId);
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

            if (loaded != null)
            {
                loaded.ProfileId = profileId;
                return loaded;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings synchronously for profile {Id}", profileId);
        }

        return CreateDefaultSettings(profileId);
    }

    private AppSettings CreateDefaultSettings(int profileId)
    {
        return new AppSettings
        {
            ProfileId = profileId,
            DownloadPath = Path.Combine(_basePath, "Downloads")
        };
    }
    
    public async Task SaveAsync()
    {
        try
        {
            var profileId = Settings.ProfileId;
            var path = GetSettingsPath(profileId);
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            
            _logger?.LogInformation("Settings saved to {Path}", path);
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }
    
    public void ResetToDefaults()
    {
        var profileId = Settings.ProfileId;
        var defaultDownloadPath = Settings.DownloadPath; // Keep download path
        _currentSettings = new AppSettings
        {
            ProfileId = profileId,
            DownloadPath = defaultDownloadPath
        };

        _ = SaveAsync();
    }
}


