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

        var settingsDir = Path.Combine(_basePath, "Settings");
        if (!Directory.Exists(settingsDir)) Directory.CreateDirectory(settingsDir);

        var newPath = Path.Combine(settingsDir, $"profile_{profileId}.json");
        
        // Migration: If file doesn't exist in new location but exists in old location, move it
        var oldPath = Path.Combine(_basePath, $"settings_profile_{profileId}.json");
        if (!File.Exists(newPath) && File.Exists(oldPath))
        {
            try
            {
                File.Move(oldPath, newPath);
                _logger?.LogInformation("Migrated profile settings from {Old} to {New}", oldPath, newPath);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to migrate profile settings for {Id}", profileId);
                return oldPath; // Fallback to old path if move fails
            }
        }
        
        return newPath;
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
            AppSettings loaded;

            if (!File.Exists(path))
            {
                _logger?.LogInformation("Settings file not found for profile {Id}, using defaults", profileId);
                loaded = CreateDefaultSettings(profileId);
            }
            else
            {
                var json = await File.ReadAllTextAsync(path);
                loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaultSettings(profileId);
            }

            // Centralization Logic: Ensure global settings are synced from profile 0
            if (profileId != 0)
            {
                var globalSettings = LoadInternalSync(0);
                SyncGlobalSettings(loaded, globalSettings);
            }

            loaded.ProfileId = profileId; // Ensure correct ID
            _currentSettings = loaded;
            
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Settings loaded for profile {profileId}: IsDarkTheme={Settings.IsDarkTheme}");
            _logger?.LogInformation("Settings loaded for profile {Id}", profileId);
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings for profile {Id}", profileId);
        }
    }

    private void SyncGlobalSettings(AppSettings target, AppSettings source)
    {
        target.IsDarkTheme = source.IsDarkTheme;
        target.Language = source.Language;
        target.AutoUpdate = source.AutoUpdate;
        target.HardwareAcceleration = source.HardwareAcceleration;
        target.Analytics = source.Analytics;
        target.AutoSelectLastProfile = source.AutoSelectLastProfile;
    }

    public async Task<AppSettings?> PeekProfileSettingsAsync(int profileId)
    {
        try
        {
            var path = GetSettingsPath(profileId);
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded != null)
            {
                loaded.ProfileId = profileId;
                // Peek should also reflect current global settings if it's not the active one
                if (profileId != 0)
                {
                    SyncGlobalSettings(loaded, LoadInternalSync(0));
                }
            }
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
            AppSettings? loaded = null;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            }

            if (loaded == null)
            {
                loaded = CreateDefaultSettings(profileId);
            }

            loaded.ProfileId = profileId;
            return loaded;
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
            
            // 1. Save current profile settings
            var path = GetSettingsPath(profileId);
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            _logger?.LogInformation("Settings saved for profile {Id}", profileId);

            // 2. If it's a sub-profile, update the master global settings too
            if (profileId != 0)
            {
                var globalPath = GetSettingsPath(0);
                var globalSettings = LoadInternalSync(0);
                
                // Only sync if actual global values changed (optimization optionally, but let's be safe)
                SyncGlobalSettings(globalSettings, Settings);
                
                var globalJson = JsonSerializer.Serialize(globalSettings, JsonOptions);
                await File.WriteAllTextAsync(globalPath, globalJson);
                _logger?.LogInformation("Global settings updated from profile {Id}", profileId);
            }
            
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


