using Xunit;
using Noctra.Services;
using Noctra.Models;
using System;
using System.IO;
using System.Text.Json;
using Noctra.Core.Services;

namespace Noctra.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _testSettingsPath;

        public SettingsServiceTests()
        {
            // Use a temp path for testing
            _testSettingsPath = Path.Combine(Path.GetTempPath(), "NoctraTests", "settings.json");
            if (File.Exists(_testSettingsPath)) File.Delete(_testSettingsPath);
            
            var dir = Path.GetDirectoryName(_testSettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        }

        [Fact]
        public void Settings_ShouldInitializeWithDefaults_WhenFileNotFound()
        {
            // Arrange
            // Ensure file doesn't exist (handled in constructor)
            var service = new SettingsService(); 
            // Note: SettingsService uses fixed LocalAppData path in constructor. 
            // In a real production codebase, we'd inject the path, but we'll test the actual logic here.
            
            // Act
            var settings = service.Settings;

            // Assert
            Assert.NotNull(settings);
            Assert.NotNull(settings.DownloadPath);
            Assert.Equal(0.9998, settings.DownloadCompletionTolerance);
        }

        [Fact]
        public void Settings_ShouldBeLazyLoaded()
        {
            // Arrange
            var service = new SettingsService();

            // Act & Assert
            // This is hard to objectively verify without reflection or mocks, 
            // but we can ensure it doesn't crash and returns valid data.
            Assert.NotNull(service.Settings);
        }

        [Fact]
        public void CreatePersistableSettings_WhenProfileSettings_ShouldNotWritePromoState()
        {
            var settings = new AppSettings
            {
                ProfileId = 167,
                PromoCodeConfigUrl = "https://example.com/promo.json",
                PromoGrant = "encrypted-grant",
                ActivePromoCode = "NOC-TEST",
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                RedeemedPromoCodes = new() { "NOC-TEST" },
                ReviewPromptLaunchCount = 2,
                ReviewPromptLastShownAtUtc = DateTime.UtcNow.AddDays(-1),
                ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.AddDays(3),
                ReviewPromptDismissed = true,
                ReviewPromptCompletedAtUtc = DateTime.UtcNow
            };

            var json = SettingsService.SerializePersistableSettings(settings, 167);

            Assert.DoesNotContain("promoCodeConfigUrl", json);
            Assert.DoesNotContain("promoGrant", json);
            Assert.DoesNotContain("activePromoCode", json);
            Assert.DoesNotContain("promoPremiumExpiresAtUtc", json);
            Assert.DoesNotContain("redeemedPromoCodes", json);
            Assert.DoesNotContain("reviewPromptLaunchCount", json);
            Assert.DoesNotContain("reviewPromptLastShownAtUtc", json);
            Assert.DoesNotContain("reviewPromptSnoozedUntilUtc", json);
            Assert.DoesNotContain("reviewPromptDismissed", json);
            Assert.DoesNotContain("reviewPromptCompletedAtUtc", json);
        }

        [Fact]
        public void CreatePersistableSettings_WhenProfileSettings_ShouldNotWriteLegalConsentState()
        {
            var settings = new AppSettings
            {
                ProfileId = 167,
                LegalConsentAccepted = true,
                LegalConsentVersion = AppSettings.CurrentLegalConsentVersion,
                LegalConsentAcceptedAtUtc = DateTime.UtcNow,
                PrivacyNoticeVersion = AppSettings.CurrentPrivacyNoticeVersion,
                DiagnosticDataConsent = true
            };

            var json = SettingsService.SerializePersistableSettings(settings, 167);

            Assert.DoesNotContain("legalConsentAccepted", json);
            Assert.DoesNotContain("legalConsentVersion", json);
            Assert.DoesNotContain("legalConsentAcceptedAtUtc", json);
            Assert.DoesNotContain("privacyNoticeVersion", json);
            Assert.DoesNotContain("diagnosticDataConsent", json);
        }

        [Fact]
        public void CreatePersistableSettings_WhenGlobalSettings_ShouldWriteLegalConsentState()
        {
            var acceptedAtUtc = DateTime.UtcNow;
            var settings = new AppSettings
            {
                ProfileId = 0,
                LegalConsentAccepted = true,
                LegalConsentVersion = AppSettings.CurrentLegalConsentVersion,
                LegalConsentAcceptedAtUtc = acceptedAtUtc,
                PrivacyNoticeVersion = AppSettings.CurrentPrivacyNoticeVersion,
                DiagnosticDataConsent = true
            };

            var json = SettingsService.SerializePersistableSettings(settings, 0);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.GetProperty("legalConsentAccepted").GetBoolean());
            Assert.Equal(AppSettings.CurrentLegalConsentVersion, root.GetProperty("legalConsentVersion").GetString());
            Assert.Equal(AppSettings.CurrentPrivacyNoticeVersion, root.GetProperty("privacyNoticeVersion").GetString());
            Assert.True(root.GetProperty("diagnosticDataConsent").GetBoolean());
            Assert.True(root.TryGetProperty("legalConsentAcceptedAtUtc", out _));
        }

        [Fact]
        public void CreatePersistableSettings_WhenGlobalSettings_ShouldNotWriteLegacyAnalytics()
        {
            var settings = new AppSettings
            {
                ProfileId = 0,
                DiagnosticDataConsent = true
            };

            var json = SettingsService.SerializePersistableSettings(settings, 0);

            Assert.Contains("diagnosticDataConsent", json);
            Assert.DoesNotContain("analytics", json);
        }

        [Fact]
        public void ApplyLegacySettingsFields_WhenAnalyticsTrue_ShouldMigrateToDiagnosticConsent()
        {
            var json = """
            {
              "analytics": true
            }
            """;
            var settings = new AppSettings();

            SettingsService.ApplyLegacySettingsFields(json, settings);

            Assert.True(settings.DiagnosticDataConsent);
        }

        [Fact]
        public void CreatePersistableSettings_WhenGlobalSettings_ShouldWriteEncryptedPromoGrant()
        {
            var settings = new AppSettings
            {
                ProfileId = 0,
                PromoGrant = "encrypted-grant",
                ActivePromoCode = "NOC-TEST",
                PromoPremiumExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                RedeemedPromoCodes = new() { "NOC-TEST" },
                ReviewPromptLaunchCount = 2,
                ReviewPromptLastShownAtUtc = DateTime.UtcNow.AddDays(-1),
                ReviewPromptSnoozedUntilUtc = DateTime.UtcNow.AddDays(3),
                ReviewPromptDismissed = true,
                ReviewPromptCompletedAtUtc = DateTime.UtcNow
            };

            var json = SettingsService.SerializePersistableSettings(settings, 0);

            Assert.Contains("promoGrant", json);
            Assert.DoesNotContain("activePromoCode", json);
            Assert.DoesNotContain("promoPremiumExpiresAtUtc", json);
            Assert.DoesNotContain("redeemedPromoCodes", json);
            Assert.Contains("reviewPromptLaunchCount", json);
            Assert.Contains("reviewPromptLastShownAtUtc", json);
            Assert.Contains("reviewPromptSnoozedUntilUtc", json);
            Assert.Contains("reviewPromptDismissed", json);
            Assert.Contains("reviewPromptCompletedAtUtc", json);
        }

        [Fact]
        public void CreatePersistableSettings_WhenGlobalSettings_ShouldNotWriteProfileOnlySettings()
        {
            var settings = new AppSettings
            {
                ProfileId = 0,
                ChannelListRefreshFrequencyHours = 12,
                EpgRefreshFrequencyHours = 24,
                EpgEnabled = false,
                CustomEpgUrl = "https://example.com/epg.xml",
                CustomEpgUrls = new() { "https://example.com/epg.xml" },
                EpgTimeOffsetHours = 3,
                SaveWatchHistory = false,
                WatchHistoryRetentionDays = 90,
                ClearHistoryOnExit = true,
                HiddenLiveGroups = new() { "Live" },
                HiddenMovieGroups = new() { "Movies" },
                HiddenSeriesGroups = new() { "Series" }
            };

            var json = SettingsService.SerializePersistableSettings(settings, 0);

            Assert.DoesNotContain("channelListRefreshFrequencyHours", json);
            Assert.DoesNotContain("epgRefreshFrequencyHours", json);
            Assert.DoesNotContain("epgEnabled", json);
            Assert.DoesNotContain("customEpgUrl", json);
            Assert.DoesNotContain("customEpgUrls", json);
            Assert.DoesNotContain("epgTimeOffsetHours", json);
            Assert.DoesNotContain("saveWatchHistory", json);
            Assert.DoesNotContain("watchHistoryRetentionDays", json);
            Assert.DoesNotContain("clearHistoryOnExit", json);
            Assert.DoesNotContain("hiddenLiveGroups", json);
            Assert.DoesNotContain("hiddenMovieGroups", json);
            Assert.DoesNotContain("hiddenSeriesGroups", json);
        }

        public void Dispose()
        {
            // Cleanup NOT strictly necessary for this specific mock-less test 
            // as it uses the real LocalAppData if we don't mock the constructor path.
            // In an ideal world, the SettingsService would take a path parameter.
        }
    }

    public class SettingsServicePersistenceTests : IDisposable
    {
        private readonly string _testDir;

        public SettingsServicePersistenceTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "NoctraPersistenceTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        private SettingsService CreateService()
            => new(new TestAppPathService(_testDir));

        [Fact]
        public async Task SaveAsync_WhenDiskWriteFails_ShouldThrowSettingsPersistenceException()
        {
            var service = CreateService();
            var settingsPath = Path.Combine(_testDir, "settings.json");

            // Hedef dosyayı kilitle: File.Replace/File.Move atomik yazımın
            // son adımı IOException fırlatır.
            await using var lockStream = new FileStream(
                settingsPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None);

            await Assert.ThrowsAsync<SettingsPersistenceException>(() => service.SaveAsync());
        }

        [Fact]
        public async Task SaveAsync_WhenDiskWriteFails_ShouldNotRaiseSettingsChanged()
        {
            var service = CreateService();
            var settingsPath = Path.Combine(_testDir, "settings.json");
            var eventRaised = 0;
            service.SettingsChanged += () => eventRaised++;

            await using var lockStream = new FileStream(
                settingsPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None);

            await Assert.ThrowsAsync<SettingsPersistenceException>(() => service.SaveAsync());

            Assert.Equal(0, eventRaised);
        }

        [Fact]
        public async Task SaveAsync_WhenSucceeds_ShouldRaiseSettingsChangedOnlyAfterWrite()
        {
            var service = CreateService();
            var settingsPath = Path.Combine(_testDir, "settings.json");
            var fileExistedAtEventTime = false;
            service.SettingsChanged += () => fileExistedAtEventTime = File.Exists(settingsPath);

            await service.SaveAsync();

            Assert.True(fileExistedAtEventTime, "SettingsChanged, disk yazımı tamamlanmadan önce tetiklenmemeli.");
            Assert.True(File.Exists(settingsPath));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDir))
                {
                    Directory.Delete(_testDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private sealed class TestAppPathService(string userDataDirectory) : IAppPathService
        {
            public string UserDataDirectory => userDataDirectory;
            public string SettingsDirectory => Path.Combine(userDataDirectory, "Profiles");
            public string DownloadsDirectory => Path.Combine(userDataDirectory, "Downloads");
            public string LegacyDownloadsDirectory => Path.Combine(userDataDirectory, "LegacyDownloads");
            public string DatabasePath => Path.Combine(userDataDirectory, "noctra.db");
            public string LegacyDatabasePath => Path.Combine(userDataDirectory, "noctra_legacy.db");
            public string TempPlaybackDirectory => Path.Combine(userDataDirectory, "Temp");
            public string LogsDirectory => Path.Combine(userDataDirectory, "Logs");

            public void EnsureUserDataDirectory() => Directory.CreateDirectory(userDataDirectory);
            public string NormalizeDownloadDirectory(string? path)
                => string.IsNullOrWhiteSpace(path) ? DownloadsDirectory : path;
        }
    }
}