using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Noctra.Models;
using Microsoft.Extensions.Logging;
using Noctra.Core.Services;

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
        
        _basePath = AppPaths.UserDataDirectory;
        
        Directory.CreateDirectory(_basePath);
        
        // Initial state
        _currentSettings = LoadInternalSync(0);
    }

    private string GetSettingsPath(int profileId)
    {
        if (profileId == 0)
            return Path.Combine(_basePath, "settings.json");

        var settingsDir = AppPaths.SettingsDirectory;
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
                ApplyLegacySettingsFields(json, loaded);
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
        target.AutoSelectLastProfile = source.AutoSelectLastProfile;
        target.PromoCodeConfigUrl = source.PromoCodeConfigUrl;
        target.PromoGrant = source.PromoGrant;
        target.LegalConsentAccepted = source.LegalConsentAccepted;
        target.LegalConsentVersion = source.LegalConsentVersion;
        target.LegalConsentAcceptedAtUtc = source.LegalConsentAcceptedAtUtc;
        target.PrivacyNoticeVersion = source.PrivacyNoticeVersion;
        target.DiagnosticDataConsent = source.DiagnosticDataConsent;
        target.ReviewPromptLaunchCount = source.ReviewPromptLaunchCount;
        target.ReviewPromptLastShownAtUtc = source.ReviewPromptLastShownAtUtc;
        target.ReviewPromptSnoozedUntilUtc = source.ReviewPromptSnoozedUntilUtc;
        target.ReviewPromptDismissed = source.ReviewPromptDismissed;
        target.ReviewPromptCompletedAtUtc = source.ReviewPromptCompletedAtUtc;
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
                ApplyLegacySettingsFields(json, loaded);
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
                if (loaded != null)
                {
                    ApplyLegacySettingsFields(json, loaded);
                }
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
            DownloadPath = AppPaths.NormalizeDownloadDirectory(null)
        };
    }

    internal static void ApplyLegacySettingsFields(string json, AppSettings settings)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (string.IsNullOrWhiteSpace(settings.PromoGrant))
            {
                settings.ActivePromoCode = ReadString(root, "activePromoCode", "ActivePromoCode");
                settings.PromoPremiumExpiresAtUtc = ReadDateTime(root, "promoPremiumExpiresAtUtc", "PromoPremiumExpiresAtUtc");
                settings.RedeemedPromoCodes = ReadStringArray(root, "redeemedPromoCodes", "RedeemedPromoCodes");
            }

            if (!settings.DiagnosticDataConsent && ReadBoolean(root, "diagnosticDataConsent", "DiagnosticDataConsent", "analytics", "Analytics") == true)
            {
                settings.DiagnosticDataConsent = true;
            }
        }
        catch
        {
            settings.ActivePromoCode = null;
            settings.PromoPremiumExpiresAtUtc = null;
            settings.RedeemedPromoCodes.Clear();
        }
    }

    private static bool? ReadBoolean(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static DateTime? ReadDateTime(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.TryGetDateTime(out var dateTime))
            {
                return dateTime;
            }
        }

        return null;
    }

    private static List<string> ReadStringArray(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!)
                    .ToList();
            }
        }

        return new List<string>();
    }
    
    public async Task SaveAsync()
    {
        try
        {
            var profileId = Settings.ProfileId;
            
            // 1. Save current profile settings
            var path = GetSettingsPath(profileId);
            var json = SerializePersistableSettings(Settings, profileId);
            await WriteAllTextAtomicallyAsync(path, json);
            _logger?.LogInformation("Settings saved for profile {Id}", profileId);

            // 2. If it's a sub-profile, update the master global settings too
            if (profileId != 0)
            {
                var globalPath = GetSettingsPath(0);
                var globalSettings = LoadInternalSync(0);
                
                // Only sync if actual global values changed (optimization optionally, but let's be safe)
                SyncGlobalSettings(globalSettings, Settings);
                
                var globalJson = SerializePersistableSettings(globalSettings, 0);
                await WriteAllTextAtomicallyAsync(globalPath, globalJson);
                _logger?.LogInformation("Global settings updated from profile {Id}", profileId);
            }
            
            SettingsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }

    internal static AppSettings CreatePersistableSettings(AppSettings source, int profileId)
    {
        var json = JsonSerializer.Serialize(source, JsonOptions);
        var copy = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        copy.ProfileId = profileId;

        if (profileId != 0)
        {
            copy.PromoCodeConfigUrl = null;
            copy.PromoGrant = null;
            copy.ActivePromoCode = null;
            copy.PromoPremiumExpiresAtUtc = null;
            copy.RedeemedPromoCodes.Clear();
            copy.LegalConsentAccepted = false;
            copy.LegalConsentVersion = null;
            copy.LegalConsentAcceptedAtUtc = null;
            copy.PrivacyNoticeVersion = null;
            copy.DiagnosticDataConsent = false;
            copy.ReviewPromptLaunchCount = 0;
            copy.ReviewPromptLastShownAtUtc = null;
            copy.ReviewPromptSnoozedUntilUtc = null;
            copy.ReviewPromptDismissed = false;
            copy.ReviewPromptCompletedAtUtc = null;
        }
        else
        {
            copy.ChannelListRefreshFrequencyHours = 0;
            copy.EpgRefreshFrequencyHours = 0;
            copy.EpgEnabled = true;
            copy.CustomEpgUrl = null;
            copy.CustomEpgUrls?.Clear();
            copy.EpgTimeOffsetHours = 0;
            copy.SaveWatchHistory = true;
            copy.WatchHistoryRetentionDays = 30;
            copy.ClearHistoryOnExit = false;
            copy.HiddenLiveGroups?.Clear();
            copy.HiddenMovieGroups?.Clear();
            copy.HiddenSeriesGroups?.Clear();
        }

        return copy;
    }

    internal static string SerializePersistableSettings(AppSettings source, int profileId)
    {
        var copy = CreatePersistableSettings(source, profileId);
        var node = JsonSerializer.SerializeToNode(copy, JsonOptions) as JsonObject ?? new JsonObject();

        if (profileId == 0)
        {
            RemoveProperties(node,
                "analytics",
                "channelListRefreshFrequencyHours",
                "epgRefreshFrequencyHours",
                "epgEnabled",
                "customEpgUrl",
                "customEpgUrls",
                "epgTimeOffsetHours",
                "saveWatchHistory",
                "watchHistoryRetentionDays",
                "clearHistoryOnExit",
                "hiddenLiveGroups",
                "hiddenMovieGroups",
                "hiddenSeriesGroups");
        }
        else
        {
            RemoveProperties(node,
                "analytics",
                "promoCodeConfigUrl",
                "promoGrant",
                "activePromoCode",
                "promoPremiumExpiresAtUtc",
                "redeemedPromoCodes",
                "legalConsentAccepted",
                "legalConsentVersion",
                "legalConsentAcceptedAtUtc",
                "privacyNoticeVersion",
                "diagnosticDataConsent",
                "reviewPromptLaunchCount",
                "reviewPromptLastShownAtUtc",
                "reviewPromptSnoozedUntilUtc",
                "reviewPromptDismissed",
                "reviewPromptCompletedAtUtc");
        }

        return JsonSerializer.Serialize(node, JsonOptions);
    }

    private static void RemoveProperties(JsonObject node, params string[] names)
    {
        foreach (var name in names)
        {
            node.Remove(name);
        }
    }

    internal static async Task WriteAllTextAtomicallyAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(contents.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup; the target file has already been protected.
            }
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

    public async Task<int> CleanOrphanedSettingsAsync(IEnumerable<int> activeProfileIds)
    {
        return await Task.Run(() =>
        {
            if (activeProfileIds == null)
            {
                _logger?.LogWarning("[SettingsService] CleanOrphanedSettingsAsync aborted. Active profile list was null.");
                return 0;
            }

            int deletedCount = 0;
            var activeIds = new HashSet<int>(activeProfileIds);
            activeIds.Add(0); // Master settings is always active

            // 1. Clean new 'Settings/' directory
            var settingsDir = Path.Combine(_basePath, "Settings");
            if (Directory.Exists(settingsDir))
            {
                var files = Directory.GetFiles(settingsDir, "profile_*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file); // e.g., "profile_1"
                        if (fileName.StartsWith("profile_", StringComparison.OrdinalIgnoreCase))
                        {
                            var idPart = fileName.Substring("profile_".Length);
                            if (int.TryParse(idPart, out var id))
                            {
                                if (!activeIds.Contains(id))
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                    _logger?.LogInformation("Deleted orphaned profile settings: {File}", file);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to check or delete orphaned setting file: {File}", file);
                    }
                }
            }

            // 2. Clean legacy root 'settings_profile_*.json' files
            try
            {
                var rootFiles = Directory.GetFiles(_basePath, "settings_profile_*.json");
                foreach (var file in rootFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file); // e.g., "settings_profile_1"
                        if (fileName.StartsWith("settings_profile_", StringComparison.OrdinalIgnoreCase))
                        {
                            var idPart = fileName.Substring("settings_profile_".Length);
                            if (int.TryParse(idPart, out var id))
                            {
                                if (!activeIds.Contains(id))
                                {
                                    File.Delete(file);
                                    deletedCount++;
                                    _logger?.LogInformation("Deleted legacy orphaned profile settings: {File}", file);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to check or delete legacy orphaned setting file: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to list legacy root settings files");
            }

            return deletedCount;
        });
    }
}
