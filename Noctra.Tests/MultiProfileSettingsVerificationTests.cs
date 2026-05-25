using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests
{
    public class MultiProfileSettingsVerificationTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;

        public MultiProfileSettingsVerificationTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }

        [Fact]
        public async Task Settings_ShouldBeProfileSpecific_AndNotInterfere()
        {
            // Bu test SettingsService'in farkli profileId'ler icin farkli dosyalar kullandigini 
            // ve ayarların birbirini ezmedigini dogrular.
            
            // Arrange
            var service = new SettingsService();
            int profileA = 1001;
            int profileB = 1002;

            // Act & Assert: Profil A ayarlarini yap ve kaydet
            await service.LoadProfileSettingsAsync(profileA);
            service.Settings.SubtitleLanguage = "tr";
            service.Settings.SubtitleFontSize = 60; // Büyük
            await service.SaveAsync();

            // Act & Assert: Profil B ayarlarini yap ve kaydet (Farkli degerler)
            await service.LoadProfileSettingsAsync(profileB);
            service.Settings.SubtitleLanguage = "en";
            service.Settings.SubtitleFontSize = 28; // Küçük
            await service.SaveAsync();

            // Tekrar Profil A'ya don ve degerlerin korundugunu dogrula
            await service.LoadProfileSettingsAsync(profileA);
            Assert.Equal("tr", service.Settings.SubtitleLanguage);
            Assert.Equal(60, service.Settings.SubtitleFontSize);

            // Tekrar Profil B'ya don ve degerlerin korundugunu dogrula
            await service.LoadProfileSettingsAsync(profileB);
            Assert.Equal("en", service.Settings.SubtitleLanguage);
            Assert.Equal(28, service.Settings.SubtitleFontSize);
        }

        [Fact]
        public async Task PeekProfileSettings_ShouldReturnCorrectSettings_WithoutChangingActiveState()
        {
            // Bu test Peek metodunun aktif profili degistirmeden arka planda baska profilin 
            // ayarlarini okuyabildigini dogrular.
            
            // Arrange
            var service = new SettingsService();
            int activeProfileId = 55;
            int otherProfileId = 99;

            await service.LoadProfileSettingsAsync(activeProfileId);
            service.Settings.SubtitleLanguage = "ActiveProfileLang";
            await service.SaveAsync();

            await service.LoadProfileSettingsAsync(otherProfileId);
            service.Settings.SubtitleLanguage = "OtherProfileLang";
            service.Settings.ClearHistoryOnExit = true;
            await service.SaveAsync();

            // Tekrar ilk profile don
            await service.LoadProfileSettingsAsync(activeProfileId);

            // Act
            var peeked = await service.PeekProfileSettingsAsync(otherProfileId);

            // Assert
            Assert.Equal(activeProfileId, service.Settings.ProfileId); // Aktif profil degismemeli
            Assert.NotNull(peeked);
            Assert.Equal("OtherProfileLang", peeked.SubtitleLanguage);
            Assert.True(peeked.ClearHistoryOnExit);
        }

        [Fact]
        public async Task MultiProfile_ExitCleanup_Logic_Verification()
        {
            // Bu test, App.axaml.cs icindeki yeni Exit mantiginin simulasyonudur.
            // Sadece ayarı acık olan profillerin temizlenecegini dogrular.
            
            // Arrange
            var contextFactoryMock = new Mock<IDbContextFactory<AppDbContext>>();
            contextFactoryMock.Setup(f => f.CreateDbContextAsync(default)).ReturnsAsync(() => new AppDbContext(_options));
            
            var settingsService = new SettingsService();
            var historyService = new WatchHistoryService(contextFactoryMock.Object, new Mock<ISettingsService>().Object);
            
            int p1_Clean = 1; // Temizlik isteyen profil
            int p2_Keep = 2;  // Gecmisi saklamak isteyen profil

            // Ayarlari hazirla
            await settingsService.LoadProfileSettingsAsync(p1_Clean);
            settingsService.Settings.ClearHistoryOnExit = true;
            await settingsService.SaveAsync();

            await settingsService.LoadProfileSettingsAsync(p2_Keep);
            settingsService.Settings.ClearHistoryOnExit = false;
            await settingsService.SaveAsync();

            // Veritabanini hazirla
            using (var db = new AppDbContext(_options))
            {
                var acc = new ProviderAccount { Name = "Acc", Url = "..." };
                db.ProviderAccounts.Add(acc);
                db.Profiles.AddRange(
                    new Profile { Id = p1_Clean, Name = "Cleaner", ProviderAccount = acc },
                    new Profile { Id = p2_Keep, Name = "Keeper", ProviderAccount = acc }
                );
                
                db.WatchHistories.Add(new WatchHistory { ProfileId = p1_Clean, WatchedAt = DateTime.UtcNow });
                db.WatchHistories.Add(new WatchHistory { ProfileId = p2_Keep, WatchedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            // Act: Simulasyon (Exit anında tum profilleri tara ve temizle)
            var allProfiles = new List<int> { p1_Clean, p2_Keep };
            foreach (var pid in allProfiles)
            {
                var s = await settingsService.PeekProfileSettingsAsync(pid);
                if (s?.ClearHistoryOnExit == true)
                {
                    await historyService.DeleteProfileHistoryAsync(pid);
                }
            }

            // Assert
            using (var db = new AppDbContext(_options))
            {
                var h1 = await db.WatchHistories.CountAsync(h => h.ProfileId == p1_Clean);
                var h2 = await db.WatchHistories.CountAsync(h => h.ProfileId == p2_Keep);

                Assert.Equal(0, h1); // P1 silinmeli
                Assert.Equal(1, h2); // P2 korunmalı
            }
        }

        [Fact]
        public async Task AtomicWrite_ShouldReplaceExistingSettingsFile_WithoutLeavingTempFile()
        {
            var directory = Path.Combine(Path.GetTempPath(), "NoctraTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");

            try
            {
                await File.WriteAllTextAsync(path, "{\"old\":true}");

                await SettingsService.WriteAllTextAtomicallyAsync(path, "{\"new\":true}");

                Assert.Equal("{\"new\":true}", await File.ReadAllTextAsync(path));
                Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); } catch { }
            }
        }
    }
}
