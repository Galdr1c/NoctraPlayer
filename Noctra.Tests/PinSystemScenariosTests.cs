using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Noctra.ViewModels;
using Xunit;

namespace Noctra.Tests
{
    public class PinSystemScenariosTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AppDbContext _context;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly Mock<IContentDownloadService> _mockDownloadService;
        private readonly Mock<ILicenseService> _mockLicenseService;
        private readonly SecurityService _securityService;
        private readonly ProfilePinService _pinService;

        public PinSystemScenariosTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            _context = new AppDbContext(_options);
            _context.Database.EnsureCreated();
            _contextFactory = new SharedConnectionDbContextFactory(_options);

            _mockDownloadService = new Mock<IContentDownloadService>();
            _mockLicenseService = new Mock<ILicenseService>();
            _securityService = new SecurityService();
            _pinService = new ProfilePinService();

            // Default to Premium for most tests
            _mockLicenseService.Setup(l => l.IsPremium).Returns(true);
            _mockLicenseService.Setup(l => l.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Close();
            _connection.Dispose();
        }

        private async Task<Profile> SeedProfileAsync(string name = "Test Profile", string? pinHash = null)
        {
            var account = new ProviderAccount { Name = name + " Account", Url = "http://test.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.Add(account);
            await _context.SaveChangesAsync();

            var profile = new Profile 
            { 
                Name = name, 
                ProviderAccount = account, 
                PinHash = pinHash,
                LastUsed = DateTime.UtcNow 
            };
            _context.Profiles.Add(profile);
            await _context.SaveChangesAsync();
            return profile;
        }

        [Fact]
        public async Task DeleteChildProfilesAsync_DeletesChildProfilesAndRelatedData_KeepsStandardProfiles()
        {
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Çocuk profilleri 1 ve 3: aynı hesabı paylaşıyor ve ikisi de silinecek
            // (kardeş kayıtlar SaveChanges sonrası denetlendiği için hesap temizlenir).
            // Çocuk profili 2: standart profille paylaşılan hesap (hesap korunur).
            // İlgisiz yetim hesap: hiçbir profile bağlı değil — scoped temizlik
            // yalnızca silinen çocuk profillerinin hesap adaylarına dokunmalı.
            var childAccount = new ProviderAccount { Name = "Child Account", Url = "http://child.com", Type = ProfileType.M3U };
            var sharedAccount = new ProviderAccount { Name = "Shared Account", Url = "http://shared.com", Type = ProfileType.M3U };
            var unrelatedOrphan = new ProviderAccount { Name = "Unrelated Orphan", Url = "http://orphan.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.AddRange(childAccount, sharedAccount, unrelatedOrphan);
            await _context.SaveChangesAsync();

            var child1 = new Profile { Name = "Child One", ProviderAccountId = childAccount.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            var child2 = new Profile { Name = "Child Two", ProviderAccountId = sharedAccount.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            var child3 = new Profile { Name = "Child Three", ProviderAccountId = childAccount.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            var standard = new Profile { Name = "Standard", ProviderAccountId = sharedAccount.Id, IsChild = false, LastUsed = DateTime.UtcNow };
            _context.Profiles.AddRange(child1, child2, child3, standard);
            await _context.SaveChangesAsync();

            // İlişkili veri: playlist + izleme geçmişi + seri ilerlemesi + EPG.
            // EpgProgram ChannelId'yi tvg-id olarak saklar ve FK'sı yoktur;
            // profil silinirken kanallarla birlikte açıkça temizlenmelidir.
            var childPlaylist = new Playlist { Name = "Child Playlist", ProfileId = child1.Id, IsActive = true };
            _context.Playlists.Add(childPlaylist);
            _context.WatchHistories.Add(new WatchHistory { ProfileId = child1.Id, WatchedAt = DateTime.UtcNow });
            _context.SeriesEpisodeProgresses.Add(new SeriesEpisodeProgress
            {
                ProfileId = child2.Id,
                SeriesKey = "s1",
                SeriesTitle = "Series",
                LastWatchedAt = DateTime.UtcNow
            });
            // Paylaşılan hesaptaki çocuk profilin (child2) kendi içerik verisi
            var child2Playlist = new Playlist { Name = "Child Two Playlist", ProfileId = child2.Id, IsActive = true };
            _context.Playlists.Add(child2Playlist);
            var standardPlaylist = new Playlist { Name = "Standard Playlist", ProfileId = standard.Id, IsActive = true };
            _context.Playlists.Add(standardPlaylist);
            await _context.SaveChangesAsync();

            var childChannel = new Channel { Name = "Child Ch", StreamUrl = "http://c", PlaylistId = childPlaylist.Id, TvgId = "child-tvg" };
            var child2Channel = new Channel { Name = "Child Two Ch", StreamUrl = "http://c2", PlaylistId = child2Playlist.Id, TvgId = "child2-tvg" };
            var standardChannel = new Channel { Name = "Standard Ch", StreamUrl = "http://s", PlaylistId = standardPlaylist.Id, TvgId = "standard-tvg" };
            _context.Channels.AddRange(childChannel, child2Channel, standardChannel);
            await _context.SaveChangesAsync();

            _context.EpgPrograms.Add(new EpgProgram
            {
                ChannelId = childChannel.TvgId,
                Title = "Child EPG",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1)
            });
            _context.EpgPrograms.Add(new EpgProgram
            {
                ChannelId = child2Channel.TvgId,
                Title = "Child Two EPG",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1)
            });
            _context.EpgPrograms.Add(new EpgProgram
            {
                ChannelId = standardChannel.TvgId,
                Title = "Standard EPG",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1)
            });
            await _context.SaveChangesAsync();

            // ImportJob kayıtlarının profil/playlist ile FK'sı yoktur — çocuk
            // profil ve playlist kayıtları için açıkça temizlenmeli; standart korunmalı.
            _context.ImportJobs.Add(new ImportJob
            {
                ProfileId = child1.Id,
                PlaylistId = childPlaylist.Id,
                SourceName = "Child Import",
                Kind = ImportJobKind.M3U,
                Status = ImportJobStatus.Completed
            });
            _context.ImportJobs.Add(new ImportJob
            {
                ProfileId = child2.Id,
                PlaylistId = child2Playlist.Id,
                SourceName = "Child Two Import",
                Kind = ImportJobKind.M3U,
                Status = ImportJobStatus.Completed
            });
            _context.ImportJobs.Add(new ImportJob
            {
                ProfileId = standard.Id,
                PlaylistId = standardPlaylist.Id,
                SourceName = "Standard Import",
                Kind = ImportJobKind.M3U,
                Status = ImportJobStatus.Completed
            });
            await _context.SaveChangesAsync();

            // Act — eski çocuk profilleri verileriyle birlikte silinir
            var deleted = await service.DeleteChildProfilesAsync();

            // Servis başka bir context örneği kullandığından izleme önbelleğini temizle.
            _context.ChangeTracker.Clear();

            // Assert — 3 çocuk profili silindi, standart korundu
            Assert.Equal(3, deleted);
            Assert.Null(await _context.Profiles.FindAsync(child1.Id));
            Assert.Null(await _context.Profiles.FindAsync(child2.Id));
            Assert.Null(await _context.Profiles.FindAsync(child3.Id));
            Assert.NotNull(await _context.Profiles.FindAsync(standard.Id));

            // İlişkili veriler temizlendi; standart profilin verisi korundu
            Assert.Empty(await _context.WatchHistories.Where(h => h.ProfileId == child1.Id).ToListAsync());
            Assert.Empty(await _context.SeriesEpisodeProgresses.Where(p => p.ProfileId == child2.Id).ToListAsync());
            Assert.Empty(await _context.Playlists.Where(p => p.ProfileId == child1.Id).ToListAsync());
            Assert.Single(await _context.Playlists.Where(p => p.ProfileId == standard.Id).ToListAsync());

            // Çocuk profilin kanalları + EPG kayıtları temizlendi; standart kaldı
            Assert.Empty(await _context.Channels.Where(c => c.PlaylistId == childPlaylist.Id).ToListAsync());
            Assert.Empty(await _context.EpgPrograms.Where(e => e.ChannelId == "child-tvg").ToListAsync());
            Assert.NotNull(await _context.Channels.FindAsync(standardChannel.Id));
            Assert.Single(await _context.EpgPrograms.Where(e => e.ChannelId == "standard-tvg").ToListAsync());

            // Çocuk profillerin (paylaşılan hesaptaki dahil) playlist/kanal/EPG
            // ve import job'ları temizlendi; standart korundu
            Assert.Empty(await _context.Playlists.Where(p => p.ProfileId == child2.Id).ToListAsync());
            Assert.Empty(await _context.Channels.Where(c => c.PlaylistId == child2Playlist.Id).ToListAsync());
            Assert.Empty(await _context.EpgPrograms.Where(e => e.ChannelId == "child2-tvg").ToListAsync());
            Assert.Empty(await _context.ImportJobs.Where(j => j.ProfileId == child1.Id || j.ProfileId == child2.Id).ToListAsync());
            Assert.Single(await _context.ImportJobs.Where(j => j.ProfileId == standard.Id).ToListAsync());

            // Yalnızca çocuk profile ait hesap silindi; paylaşılan hesap ve
            // ilgisiz yetim hesap korundu (temizlik adaylarla sınırlı)
            Assert.Null(await _context.ProviderAccounts.FindAsync(childAccount.Id));
            Assert.NotNull(await _context.ProviderAccounts.FindAsync(sharedAccount.Id));
            Assert.NotNull(await _context.ProviderAccounts.FindAsync(unrelatedOrphan.Id));
        }

        [Fact]
        public async Task DeleteChildProfilesAsync_IsIdempotent_SecondCallReturnsZero()
        {
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var account = new ProviderAccount { Name = "Idempotent Account", Url = "http://idem.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.Add(account);
            await _context.SaveChangesAsync();

            var child = new Profile { Name = "Child", ProviderAccountId = account.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            _context.Profiles.Add(child);
            await _context.SaveChangesAsync();

            Assert.Equal(1, await service.DeleteChildProfilesAsync());
            _context.ChangeTracker.Clear();

            // İkinci çağrı idempotent: silinecek çocuk profil kalmadı → 0 döner
            Assert.Equal(0, await service.DeleteChildProfilesAsync());
            Assert.Null(await _context.Profiles.FindAsync(child.Id));
        }

        [Fact]
        public async Task DeleteChildProfilesAsync_DownloadFailure_PreservesProfileAndAccount()
        {
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var account = new ProviderAccount { Name = "DL Account", Url = "http://dl.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.Add(account);
            await _context.SaveChangesAsync();

            var child = new Profile { Name = "Child", ProviderAccountId = account.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            _context.Profiles.Add(child);
            await _context.SaveChangesAsync();

            _mockDownloadService
                .Setup(d => d.DeleteProfileDownloadsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("download delete failed"));

            // İndirme silme hatası atomik DB silme işlemini durdurmalı —
            // profil ve hesap korunur, bir sonraki açılışta tekrar denenir.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteChildProfilesAsync());

            _context.ChangeTracker.Clear();
            Assert.NotNull(await _context.Profiles.FindAsync(child.Id));
            Assert.NotNull(await _context.ProviderAccounts.FindAsync(account.Id));
        }

        [Fact]
        public async Task DeleteChildProfilesAsync_SettingsCleanupFailure_StillReturnsCountAndDeletes()
        {
            var settings = new AppSettings();
            var settingsMock = new Mock<ISettingsService>();
            settingsMock.SetupGet(s => s.Settings).Returns(settings);
            settingsMock
                .Setup(s => s.CleanOrphanedSettingsAsync(It.IsAny<IEnumerable<int>>()))
                .ThrowsAsync(new InvalidOperationException("settings cleanup failed"));

            var service = new ProfileService(
                _contextFactory,
                _mockDownloadService.Object,
                _mockLicenseService.Object,
                settingsMock.Object);

            var account = new ProviderAccount { Name = "Settings Account", Url = "http://settings.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.Add(account);
            await _context.SaveChangesAsync();

            var child = new Profile { Name = "Child", ProviderAccountId = account.Id, IsChild = true, LastUsed = DateTime.UtcNow };
            _context.Profiles.Add(child);
            await _context.SaveChangesAsync();

            // DB silme başarılı + ayar temizliği başarısız → silinen sayı yine
            // dönmeli VE bir defalık bildirim bayrağı, silme sonucu alındığı
            // anda servis tarafından kalıcı olarak yazılmış olmalı (temizlik
            // hatası bildirimi kaybettirmemeli).
            var deleted = await service.DeleteChildProfilesAsync();
            Assert.Equal(1, deleted);
            Assert.True(settings.ChildModeRemovedNoticePending);

            _context.ChangeTracker.Clear();
            Assert.Null(await _context.Profiles.FindAsync(child.Id));
        }

        [Fact]
        public async Task DeleteChildProfilesAsync_DeletesChildProfileThatIsAlsoExpiredPendingDeletion()
        {
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var account = new ProviderAccount { Name = "Both Account", Url = "http://both.com", Type = ProfileType.M3U };
            _context.ProviderAccounts.Add(account);
            await _context.SaveChangesAsync();

            // Çocuk profil aynı zamanda süresi dolmuş pending-deletion — hangi
            // bakım adımı önce çalışırsa çalışsın çocuk geçişi onu temizler.
            var child = new Profile
            {
                Name = "Child Expired",
                ProviderAccountId = account.Id,
                IsChild = true,
                PendingDeletionAt = DateTime.UtcNow.AddDays(-4),
                LastUsed = DateTime.UtcNow
            };
            _context.Profiles.Add(child);
            await _context.SaveChangesAsync();

            Assert.Equal(1, await service.DeleteChildProfilesAsync());

            _context.ChangeTracker.Clear();
            Assert.Null(await _context.Profiles.FindAsync(child.Id));
        }

        [Fact]
        public async Task PinCreation_SavesCorrectHash_WhenValid()
        {
            // Arrange
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            var pin = "1234";
            var expectedHash = _pinService.CreateVerifier(pin);

            var request = new ProfileSaveRequest
            {
                ProfileName = "PIN Profile",
                Avatar = "default",
                Url = "http://test.com",
                Username = "test",
                EncryptedPassword = "test",
                AccountType = ProfileType.M3U,
                PinHash = expectedHash
            };

            // Act
            var profile = await service.SaveProfileAsync(request);

            // Assert
            Assert.NotNull(profile);
            Assert.Equal(expectedHash, profile.PinHash);
            
            // Verify verification works
            Assert.True(_pinService.Verify(pin, profile.PinHash));
        }

        [Fact]
        public async Task PremiumGate_VerifiedInViewModel_BlocksPinInteraction()
        {
            // Arrange
            _mockLicenseService.Setup(l => l.IsPremium).Returns(false); // Not premium
            
            var mockAvatarService = new Mock<IAvatarService>();
            mockAvatarService.Setup(s => s.GetAvatarsByCategory())
                .Returns(new Dictionary<string, List<string>> { { "All", new List<string> { "avatar_1" } } });

            var vm = new AddProfileViewModel(
                new Mock<IProfileService>().Object,
                new Mock<IDispatcherService>().Object,
                mockAvatarService.Object,
                new Mock<IDialogService>().Object,
                _mockLicenseService.Object,
                new Mock<IM3UParser>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                _securityService,
                _pinService,
                new Mock<ILocalizationService>().Object);

            // Assert
            Assert.False(vm.IsPinAvailable, "PIN should not be available for non-premium users.");
        }

        [Fact]
        public async Task PremiumExpired_EditProfileSave_PreservesExistingPinHash()
        {
            // Arrange
            _mockLicenseService.Setup(l => l.IsPremium).Returns(false);
            _mockLicenseService.Setup(l => l.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>())).Returns(true);

            var oldPin = "1234";
            var oldHash = _pinService.CreateVerifier(oldPin);
            var profile = await SeedProfileAsync("Premium Expired", oldHash);

            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var mockAvatarService = new Mock<IAvatarService>();
            mockAvatarService.Setup(s => s.GetAvatarsByCategory())
                .Returns(new Dictionary<string, List<string>> { { "All", new List<string> { "avatar_1" } } });

            var vm = new AddProfileViewModel(
                service,
                new Mock<IDispatcherService>().Object,
                mockAvatarService.Object,
                new Mock<IDialogService>().Object,
                _mockLicenseService.Object,
                new Mock<IM3UParser>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                _securityService,
                _pinService,
                new Mock<ILocalizationService>().Object);

            vm.InitializeForEdit(profile);
            Assert.True(vm.HasPin, "Existing PIN must surface as HasPin when editing.");

            // Düzenleme ekranı her zaman merkezî bir grant ile açılır
            vm.AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Edit);

            // Act — non-premium user edits profile details only
            vm.ProfileName = "Renamed Profile";
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — the existing PIN must be preserved, not silently removed
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile);
            Assert.Equal(oldHash, updatedProfile!.PinHash);
            Assert.True(_pinService.Verify(oldPin, updatedProfile.PinHash));
        }

        [Fact]
        public async Task PinManagement_ChangePin_UpdatesHash()
        {
            // Arrange
            var oldPin = "1111";
            var oldHash = _pinService.CreateVerifier(oldPin);
            var profile = await SeedProfileAsync("Old PIN", oldHash);
            
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            
            var newPin = "9999";
            var newHash = _pinService.CreateVerifier(newPin);

            var request = new ProfileSaveRequest
            {
                ExistingIds = new ExistingProfileIds ( profile.Id, profile.ProviderAccountId ),
                ProfileName = "New PIN",
                Avatar = "default",
                Url = "http://test.com",
                Username = "test",
                EncryptedPassword = "test",
                AccountType = ProfileType.M3U,
                PinHash = newHash,
                AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.PinChange)
            };

            // Act
            await service.SaveProfileAsync(request);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(newHash, updatedProfile!.PinHash);
            Assert.True(_pinService.Verify(newPin, updatedProfile.PinHash));
            Assert.False(_pinService.Verify(oldPin, updatedProfile.PinHash));
        }

        [Fact]
        public async Task PinManagement_RemovePin_SetsHashToNull()
        {
            // Arrange
            var profile = await SeedProfileAsync("PIN to Remove", _pinService.CreateVerifier("1234"));
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var request = new ProfileSaveRequest
            {
                ExistingIds = new ExistingProfileIds ( profile.Id, profile.ProviderAccountId ),
                ProfileName = "No PIN",
                Avatar = "default",
                Url = "http://test.com",
                Username = "test",
                EncryptedPassword = "test",
                AccountType = ProfileType.M3U,
                PinHash = null, // Explicitly remove
                AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.PinChange)
            };

            // Act
            await service.SaveProfileAsync(request);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Null(updatedProfile!.PinHash);
        }

        [Fact]
        public async Task PinLockout_FiveFailures_LocksProfilePersistently()
        {
            // Arrange
            var profile = await SeedProfileAsync("Locked", _pinService.CreateVerifier("1234"));
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Act — four failures stay unlocked, fifth locks
            for (var i = 1; i <= 4; i++)
            {
                var state = await service.RegisterPinFailureAsync(profile.Id);
                Assert.False(state.IsLocked, $"Attempt {i} must not lock yet.");
                Assert.Equal(i, state.FailedPinAttempts);
            }

            var locked = await service.RegisterPinFailureAsync(profile.Id);

            // Assert — locked with ~30s remaining
            Assert.True(locked.IsLocked);
            Assert.NotNull(locked.PinLockedUntilUtc);
            Assert.True(locked.RemainingLockDuration!.Value.TotalSeconds is > 25 and <= 30);

            // Persistence — a brand-new context (simulating app restart) still sees the lock
            using var dbVerify = _contextFactory.CreateDbContext();
            var persisted = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(persisted!.PinLockedUntilUtc);
            Assert.Equal(5, persisted.FailedPinAttempts);

            var afterRestart = await service.GetPinVerificationStateAsync(profile.Id);
            Assert.True(afterRestart.IsLocked, "Lock must survive an app restart.");
        }

        [Fact]
        public async Task PinLockout_SuccessfulVerification_ResetsAttempts()
        {
            // Arrange
            var profile = await SeedProfileAsync("Reset", _pinService.CreateVerifier("1234"));
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var state = await service.RegisterPinFailureAsync(profile.Id);
            state = await service.RegisterPinFailureAsync(profile.Id);
            Assert.Equal(2, state.FailedPinAttempts);

            // Act — successful verification clears the counter
            await service.ResetPinAttemptsAsync(profile.Id);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updated = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(0, updated!.FailedPinAttempts);
            Assert.Null(updated.PinLockedUntilUtc);
        }

        [Fact]
        public async Task PinLockout_ExpiredLock_IsClearedOnRead()
        {
            // Arrange
            var profile = await SeedProfileAsync("Expired Lock", _pinService.CreateVerifier("1234"));
            profile.FailedPinAttempts = 5;
            profile.PinLockedUntilUtc = DateTime.UtcNow.AddSeconds(-1);
            _context.Entry(profile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Act
            var state = await service.GetPinVerificationStateAsync(profile.Id);

            // Assert — expired lock is cleared and the counter reset
            Assert.False(state.IsLocked);
            Assert.Equal(0, state.FailedPinAttempts);

            using var dbVerify = _contextFactory.CreateDbContext();
            var updated = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Null(updated!.PinLockedUntilUtc);
            Assert.Equal(0, updated.FailedPinAttempts);
        }

        [Fact]
        public void PinEntry_StartsLocked_WhenLockedUntilInFuture()
        {
            // Arrange — using: lockout sayacı test sonunda iptal edilir, aksi
            // halde 30 saniyelik detached Task.Run test paketinde yaşamaya devam eder.
            using var vm = new PinEntryViewModel(
                new Mock<IProfileService>().Object,
                new Mock<IDispatcherService>().Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object,
                failedAttempts: ProfileService.MaxPinAttempts,
                lockedUntilUtc: DateTime.UtcNow.AddSeconds(30));

            // Assert
            Assert.True(vm.IsLocked);
            Assert.True(vm.LockSecondsRemaining > 0);

            // Act — input must be blocked while locked
            vm.PressDigitCommand.Execute("1");

            // Assert
            Assert.Equal(string.Empty, vm.EnteredPin);
        }

        [Fact]
        public void PinEntry_LockoutMessageText_CombinesReasonAndCountdown()
        {
            // Arrange — Türkçe format: kilitlenme nedeni (çok fazla yanlış PIN)
            // ile kalan süre birlikte gösterilir; sayaç tek başına kalmaz.
            var localization = new Mock<ILocalizationService>();
            localization
                .Setup(service => service.GetString("PinEntry.Error.ProfileLockedFormat"))
                .Returns("Çok fazla yanlış PIN girdiniz. {0} saniye sonra tekrar deneyebilirsiniz.");

            var vm = new PinEntryViewModel(
                new Mock<IProfileService>().Object,
                new Mock<IDispatcherService>().Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                localization.Object,
                failedAttempts: ProfileService.MaxPinAttempts,
                lockedUntilUtc: DateTime.UtcNow.AddSeconds(30));

            using (vm)
            {
                // Act
                var text = vm.LockoutMessageText;

                // Assert — neden ve saniye sayısı birlikte yer alır
                Assert.True(vm.IsLocked);
                Assert.Contains(vm.LockSecondsRemaining.ToString(), text);
                Assert.Contains("Çok fazla yanlış PIN girdiniz", text);
                Assert.Contains("saniye sonra tekrar deneyebilirsiniz", text);

                // Hata metni ayrıca çizilmez (UI çakışması olmaz); birleşik
                // mesaj kilit nedeni + geri sayımı birlikte sunar.
                Assert.False(vm.ShowErrorMessage);
                Assert.False(string.IsNullOrEmpty(text));
            }
        }

        [Fact]
        public void PinEntry_PinProgressA11yText_UsesLocalizedFormat()
        {
            // Arrange — format: {0}=toplam hane, {1}=girilen hane
            var localization = new Mock<ILocalizationService>();
            localization
                .Setup(service => service.GetString("PinEntry.ProgressA11yFormat"))
                .Returns("{1} of {0} digits entered");

            var vm = new PinEntryViewModel(
                new Mock<IProfileService>().Object,
                new Mock<IDispatcherService>().Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                localization.Object);

            using (vm)
            {
                // Boşken
                Assert.Equal("0 of 4 digits entered", vm.PinProgressA11yText);

                // Rakam girdikçe ilerleme güncellenir
                foreach (var digit in "12")
                {
                    vm.PressDigitCommand.Execute(digit.ToString());
                }
                Assert.Equal("2 of 4 digits entered", vm.PinProgressA11yText);

                // Geri silme ilerlemeyi azaltır
                vm.BackspaceCommand.Execute(null);
                Assert.Equal("1 of 4 digits entered", vm.PinProgressA11yText);
            }
        }

        [Fact]
        public async Task PinEntry_Dispose_CancelsLockoutCountdown()
        {
            // Arrange
            var dispatcher = new Mock<IDispatcherService>();
            var vm = new PinEntryViewModel(
                new Mock<IProfileService>().Object,
                dispatcher.Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object,
                failedAttempts: ProfileService.MaxPinAttempts,
                lockedUntilUtc: DateTime.UtcNow.AddSeconds(30));

            // Act — countdown başladıktan hemen sonra VM elden çıkarılır
            vm.Dispose();

            // İptal çalışmasaydı ilk tick 1 saniyede gelirdi; 2.5 sn sonra hiç
            // UI güncellemesi gelmediyse sayac iptal edilmiş demektir.
            await Task.Delay(2500);

            // Assert
            dispatcher.Verify(
                service => service.BeginInvoke(It.IsAny<Action>()),
                Times.Never);
        }

        [Fact]
        public async Task PinEntry_LockoutCountdown_UnlocksAfterDuration()
        {
            // Arrange — dispatcher tick'lerini senkron uygular
            var dispatcher = new Mock<IDispatcherService>();
            dispatcher
                .Setup(service => service.BeginInvoke(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var vm = new PinEntryViewModel(
                new Mock<IProfileService>().Object,
                dispatcher.Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object,
                failedAttempts: ProfileService.MaxPinAttempts,
                lockedUntilUtc: DateTime.UtcNow.AddSeconds(3));

            using (vm)
            {
                Assert.True(vm.IsLocked);
                Assert.True(vm.LockSecondsRemaining >= 1);

                // Geri sayım bitene kadar bekle (yoğun makinelerde tick gecikebilir)
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (vm.IsLocked && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200);
                }

                Assert.False(vm.IsLocked);
                Assert.Equal(0, vm.LockSecondsRemaining);
            }
        }

        [Fact]
        public async Task PinEntry_ResumesAttemptCount_FromPersistedState()
        {
            // Arrange — veritabanında 4 kalıcı hata olan profil; UI bunu devralır.
            var verifier = _pinService.CreateVerifier("1234");
            var profile = await SeedProfileAsync("Persisted State", verifier);
            profile.FailedPinAttempts = 4;
            _context.Entry(profile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var localization = new Mock<ILocalizationService>();
            localization.Setup(s => s.GetString(It.IsAny<string>())).Returns((string key) => key);

            var vm = new PinEntryViewModel(
                service,
                new Mock<IDispatcherService>().Object,
                profile.Id,
                verifier,
                "Test",
                string.Empty,
                "Login",
                localization.Object,
                failedAttempts: 4);

            var failed = 0;
            vm.AttemptFailed += (_, count) => failed = count;

            using (vm)
            {
                // Act — 4 kalıcı hatanın ardından 5. yanlış giriş (atomik servis çağrısı)
                vm.PressDigitCommand.Execute("1");
                vm.PressDigitCommand.Execute("1");
                vm.PressDigitCommand.Execute("1");
                await vm.PressDigitCommand.ExecuteAsync("1");

                // Assert — 5. hata kilidi kalıcılaştırır ve UI kilitlenir;
                // sayaç artık veritabanından gelir (UI yerel sayacına güvenmez).
                Assert.Equal(5, failed);
                Assert.True(vm.IsLocked);
                Assert.True(vm.LockSecondsRemaining > 0);
                Assert.False(vm.ShowErrorMessage);
            }

            // Kalıcılık — uygulama yeniden açılsa bile kilit sürer.
            using var dbVerify = _contextFactory.CreateDbContext();
            var persisted = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(5, persisted!.FailedPinAttempts);
            Assert.NotNull(persisted.PinLockedUntilUtc);
        }

        [Fact]
        public async Task PinEntry_CorrectPin_VerifiesAtomically()
        {
            // Arrange — servis doğru PIN için atomik başarı döndürür (sayaç sıfırlanır)
            var profileService = new Mock<IProfileService>();
            profileService
                .Setup(s => s.VerifyAttemptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new ProfilePinAttemptResult(true, new PinVerificationState(0, null, false, null)));

            var vm = new PinEntryViewModel(
                profileService.Object,
                new Mock<IDispatcherService>().Object,
                1, // profileId
                _pinService.CreateVerifier("1234"),
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object);

            bool? result = null;
            vm.PinResult += (_, value) => result = value;

            using (vm)
            {
                // Act — 4. rakam doğrulamayı başlatır; servis dönünce sonuç gelir
                vm.PressDigitCommand.Execute("1");
                vm.PressDigitCommand.Execute("2");
                vm.PressDigitCommand.Execute("3");
                await vm.PressDigitCommand.ExecuteAsync("4");

                // Assert — doğru PIN anında döner, keypad yeniden etkinleşir
                Assert.True(result);
                Assert.Equal("1234", vm.EnteredPin);
                Assert.True(vm.IsKeypadEnabled);
                Assert.False(vm.IsVerifying);
            }
        }

        [Fact]
        public async Task PinEntry_LegacyFormatHash_IsRejected()
        {
            // Arrange — eski (PBKDF2/legacy SHA-256) hash'ler PIN2'ye geçişte
            // sıfırlanır; atomik doğrulama artık bu formatları kabul etmez.
            const string legacyHashFor1234 = "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88";
            var profile = await SeedProfileAsync("Legacy", legacyHashFor1234);
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var localization = new Mock<ILocalizationService>();
            localization.Setup(s => s.GetString(It.IsAny<string>())).Returns((string key) => key);

            var vm = new PinEntryViewModel(
                service,
                new Mock<IDispatcherService>().Object,
                profile.Id,
                legacyHashFor1234,
                "Test",
                string.Empty,
                "Login",
                localization.Object);

            int? failedAttempts = null;
            vm.AttemptFailed += (_, count) => failedAttempts = count;

            using (vm)
            {
                // Act
                vm.PressDigitCommand.Execute("1");
                vm.PressDigitCommand.Execute("2");
                vm.PressDigitCommand.Execute("3");
                await vm.PressDigitCommand.ExecuteAsync("4");

                // Assert — legacy format başarısız deneme olarak sayılır, giriş temizlenir
                Assert.Equal(1, failedAttempts);
                Assert.Equal(string.Empty, vm.EnteredPin);
                Assert.Equal("PinEntry.Error.WrongPinRemainingFormat", vm.ErrorMessage);
            }
        }

        [Fact]
        public async Task SchemaFixup_ResetsLegacyPinHashes()
        {
            // Arrange — eski PBKDF2 ve legacy SHA-256 kayıtları + güncel PIN2 kaydı
            var legacyPbkdf2 = await SeedProfileAsync("Legacy PBKDF2", "PBKDF2$SHA256$210000$c2FsdA==$aGFzaA==");
            var legacySha256 = await SeedProfileAsync("Legacy SHA-256", "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88");
            var currentPin = await SeedProfileAsync("Current PIN", _pinService.CreateVerifier("1234"));
            var pinless = await SeedProfileAsync("Pinless", null);

            var fixup = new DatabaseSchemaFixupService();

            // Act — Seçenek A: legacy hash'ler bir defalık sıfırlanır
            var resetCount = await fixup.ApplyAsync(_context, DatabaseSchemaFixupProfile.Desktop);

            // Assert — yalnızca legacy kayıtlar sıfırlandı; PIN2 ve PIN'siz korundu
            Assert.Equal(2, resetCount);

            using var dbVerify = _contextFactory.CreateDbContext();
            Assert.Null((await dbVerify.Profiles.FindAsync(legacyPbkdf2.Id))!.PinHash);
            Assert.Null((await dbVerify.Profiles.FindAsync(legacySha256.Id))!.PinHash);
            var preserved = await dbVerify.Profiles.FindAsync(currentPin.Id);
            Assert.True(ProfilePinVerifier.IsCurrentFormat(preserved!.PinHash));
            Assert.True(_pinService.Verify("1234", preserved.PinHash));
            Assert.Null((await dbVerify.Profiles.FindAsync(pinless.Id))!.PinHash);
        }

        [Fact]
        public void DeletionUrgency_IsContinuousAcross24HourBoundary()
        {
            // 25 saat kala → 0.6'nın hemen üzerinde (eski davranış 1.0 atlardı)
            var profile = new Profile
            {
                PendingDeletionAt = DateTime.UtcNow.AddHours(25 - 72)
            };
            var justAbove24h = profile.DeletionUrgency;
            Assert.InRange(justAbove24h, 0.60, 0.65);
            Assert.True(justAbove24h < 1.0, "24 saat sınırının hemen üzerinde 1.0'a sıçramamalı");

            // 24 saat kala → 0.6 civarı (sürekli geçiş)
            profile.PendingDeletionAt = DateTime.UtcNow.AddHours(24 - 72);
            Assert.InRange(profile.DeletionUrgency, 0.58, 0.61);

            // 12 saat kala → 0.3 civarı
            profile.PendingDeletionAt = DateTime.UtcNow.AddHours(12 - 72);
            Assert.InRange(profile.DeletionUrgency, 0.28, 0.31);
        }

        [Fact]
        public void DeletionUrgency_ClampsAtEnds()
        {
            // 72 saat ve üzeri → 1.0 (normal)
            var fresh = new Profile { PendingDeletionAt = DateTime.UtcNow.AddHours(1) };
            Assert.Equal(1.0, fresh.DeletionUrgency);

            // Süre dolmuş → 0.0 (tam kırmızı)
            var expired = new Profile { PendingDeletionAt = DateTime.UtcNow.AddHours(-72) };
            Assert.Equal(0.0, expired.DeletionUrgency);

            // Silme yok → 1.0 (renk normal kalır)
            Assert.Equal(1.0, new Profile().DeletionUrgency);
        }

        [Fact]
        public async Task DeletionLifecycle_Schedule_SetsPendingAt()
        {
            // Arrange
            var profile = await SeedProfileAsync("To Delete");
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Act
            await service.ScheduleProfileDeletionAsync(profile.Id);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile!.PendingDeletionAt);
            Assert.True(updatedProfile.IsPendingDeletion);
        }

        [Fact]
        public async Task DeletionLifecycle_ScheduleRepeated_DoesNotResetTimer()
        {
            // Arrange
            var profile = await SeedProfileAsync("Timer Stability");
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Initial schedule
            await service.ScheduleProfileDeletionAsync(profile.Id);
            
            DateTime? firstTime;
            using (var db1 = _contextFactory.CreateDbContext())
            {
                firstTime = (await db1.Profiles.FindAsync(profile.Id))!.PendingDeletionAt;
            }

            // Wait a tiny bit (simulated)
            await Task.Delay(10);

            // Act - Schedule again
            await service.ScheduleProfileDeletionAsync(profile.Id);

            // Assert
            using (var db2 = _contextFactory.CreateDbContext())
            {
                var secondTime = (await db2.Profiles.FindAsync(profile.Id))!.PendingDeletionAt;
                Assert.Equal(firstTime, secondTime);
            }
        }

        [Fact]
        public async Task DeletionLifecycle_Purge_RemovesExpiredProfiles()
        {
            // Arrange
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            
            var expiredProfile = await SeedProfileAsync("Expired");
            expiredProfile.PendingDeletionAt = DateTime.UtcNow.AddDays(-4); // > 3 days ago
            _context.Entry(expiredProfile).State = EntityState.Modified; // Ensure saved
            await _context.SaveChangesAsync();

            // Süresi dolmuş profile playlist + kanal + EPG ekle — EPG'nin FK'sı
            // yoktur ve purge sırasında açıkça temizlenmelidir.
            var expiredPlaylist = new Playlist { Name = "Expired Playlist", ProfileId = expiredProfile.Id, IsActive = true };
            _context.Playlists.Add(expiredPlaylist);
            await _context.SaveChangesAsync();
            var expiredChannel = new Channel
            {
                Name = "Expired Ch",
                StreamUrl = "http://e",
                PlaylistId = expiredPlaylist.Id,
                TvgId = "expired-tvg"
            };
            _context.Channels.Add(expiredChannel);
            await _context.SaveChangesAsync();
            _context.EpgPrograms.Add(new EpgProgram
            {
                ChannelId = expiredChannel.TvgId,
                Title = "Expired EPG",
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddHours(1)
            });
            _context.ImportJobs.Add(new ImportJob
            {
                ProfileId = expiredProfile.Id,
                SourceName = "Expired Import",
                Kind = ImportJobKind.M3U,
                Status = ImportJobStatus.Completed
            });
            await _context.SaveChangesAsync();

            var freshProfile = await SeedProfileAsync("Freshly Scheduled");
            freshProfile.PendingDeletionAt = DateTime.UtcNow; // Scheduled just now
            _context.Entry(freshProfile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            var normalProfile = await SeedProfileAsync("Normal"); // No deletion scheduled

            // Act
            await service.PurgeExpiredProfilesAsync();

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            Assert.Null(await dbVerify.Profiles.FindAsync(expiredProfile.Id)); // Deleted
            Assert.NotNull(await dbVerify.Profiles.FindAsync(freshProfile.Id)); // Still there

            // Süresi dolmuş profilin playlist/kanal/EPG/import-job verisi de temizlendi
            Assert.Empty(await dbVerify.Playlists.Where(p => p.ProfileId == expiredProfile.Id).ToListAsync());
            Assert.Empty(await dbVerify.Channels.Where(c => c.PlaylistId == expiredPlaylist.Id).ToListAsync());
            Assert.Empty(await dbVerify.EpgPrograms.Where(e => e.ChannelId == "expired-tvg").ToListAsync());
            Assert.Empty(await dbVerify.ImportJobs.Where(j => j.ProfileId == expiredProfile.Id).ToListAsync());
            Assert.NotNull(await dbVerify.Profiles.FindAsync(normalProfile.Id)); // Still there
        }

        [Fact]
        public async Task CancellationFlow_Cancel_ClearsPendingAt()
        {
            // Arrange
            var profile = await SeedProfileAsync("Rescued");
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            await service.ScheduleProfileDeletionAsync(profile.Id);

            // Act
            await service.CancelProfileDeletionAsync(profile.Id);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Null(updatedProfile!.PendingDeletionAt);
            Assert.False(updatedProfile.IsPendingDeletion);
        }

        [Fact]
        public async Task PendingDeletion_EditProfileSave_CancelsDeletion()
        {
            // Arrange — PIN korumalı, silinme geri sayımındaki profil
            var profile = await SeedProfileAsync("Doomed Profile", _pinService.CreateVerifier("1234"));
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            await service.ScheduleProfileDeletionAsync(profile.Id);

            using var dbLoad = _contextFactory.CreateDbContext();
            var doomed = await dbLoad.Profiles
                .Include(p => p.ProviderAccount)
                .FirstAsync(p => p.Id == profile.Id);
            Assert.True(doomed.IsPendingDeletion);

            var mockAvatarService = new Mock<IAvatarService>();
            mockAvatarService.Setup(s => s.GetAvatarsByCategory())
                .Returns(new Dictionary<string, List<string>> { { "All", new List<string> { "avatar_1" } } });

            var vm = new AddProfileViewModel(
                service,
                new Mock<IDispatcherService>().Object,
                mockAvatarService.Object,
                new Mock<IDialogService>().Object,
                _mockLicenseService.Object,
                new Mock<IM3UParser>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                _securityService,
                _pinService,
                new Mock<ILocalizationService>().Object);

            vm.InitializeForEdit(doomed);
            Assert.True(vm.EditingProfile!.IsPendingDeletion);
            vm.AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Edit);

            // Act — yönetim modu: doğru PIN kanıtlandı, profil düzenlendi ve kaydedildi
            vm.ProfileName = "Rescued By Edit";
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — üç günlük silme iptal edilmiş olmalı
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile);
            Assert.Null(updatedProfile!.PendingDeletionAt);
            Assert.False(updatedProfile.IsPendingDeletion);
            Assert.Equal("Rescued By Edit", updatedProfile.Name);
        }

        [Fact]
        public async Task Save_WeakPin_DeclinedByUser_DoesNotSave()
        {
            // Arrange
            var dialog = new Mock<IDialogService>();
            dialog.Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(false);
            var profileService = new Mock<IProfileService>();
            var vm = CreatePinSetupViewModel(profileService.Object, dialog.Object);

            vm.HasPin = true;
            vm.PinCode = "1234";
            vm.PinConfirm = "1234";

            // Act — user declines the weak-PIN warning
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — nothing was persisted
            profileService.Verify(
                s => s.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()),
                Times.Never);
        }

        [Fact]
        public async Task Save_WeakPin_ConfirmedByUser_Saves()
        {
            // Arrange
            var dialog = new Mock<IDialogService>();
            dialog.Setup(s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(true);
            var profileService = new Mock<IProfileService>();
            var vm = CreatePinSetupViewModel(profileService.Object, dialog.Object);

            vm.HasPin = true;
            vm.PinCode = "1234";
            vm.PinConfirm = "1234";

            // Act — user accepts the weak-PIN warning
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — the PIN is saved as requested
            profileService.Verify(
                s => s.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()),
                Times.Once);
        }

        [Fact]
        public async Task Save_StrongPin_NoConfirmationDialog_Saves()
        {
            // Arrange
            var dialog = new Mock<IDialogService>();
            var profileService = new Mock<IProfileService>();
            var vm = CreatePinSetupViewModel(profileService.Object, dialog.Object);

            vm.HasPin = true;
            vm.PinCode = "4837";
            vm.PinConfirm = "4837";

            // Act
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — no warning is shown, save proceeds
            dialog.Verify(
                s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);
            profileService.Verify(
                s => s.SaveProfileAsync(It.IsAny<ProfileSaveRequest>()),
                Times.Once);
        }

        [Fact]
        public async Task Save_EditWithoutNewPin_KeepsExistingHash_NoWarning()
        {
            // Arrange — PIN korumalı profil; düzenlemede yeni PIN girilmiyor
            var profile = await SeedProfileAsync("Existing PIN", _pinService.CreateVerifier("1234"));
            using var dbLoad = _contextFactory.CreateDbContext();
            var existing = await dbLoad.Profiles
                .Include(p => p.ProviderAccount)
                .FirstAsync(p => p.Id == profile.Id);

            // M3U kuralına uygun URL — böylece düzenlemede kimlik bilgileri
            // değişmez ve provider doğrulama adımı tetiklenmez.
            existing.ProviderAccount.Url = "http://test.com/playlist.m3u";
            await dbLoad.SaveChangesAsync();

            var dialog = new Mock<IDialogService>();
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            var vm = CreatePinSetupViewModel(service, dialog.Object);
            vm.InitializeForEdit(existing);
            vm.AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Edit);
            vm.ProfileName = "Renamed";

            // Act — PinCode stays empty (existing hash preserved)
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — existing weak PIN is not re-flagged on unrelated edits
            dialog.Verify(
                s => s.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>()),
                Times.Never);

            // Assert — the existing hash is preserved, not re-hashed or removed
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile);
            Assert.Equal(existing.PinHash, updatedProfile!.PinHash);
            Assert.Equal("Renamed", updatedProfile.Name);
        }

        [Fact]
        public void PinInput_NormalizesPastedSpacesAndUnicodeDigits()
        {
            // Arrange
            var dialog = new Mock<IDialogService>();
            var profileService = new Mock<IProfileService>();
            var vm = CreatePinSetupViewModel(profileService.Object, dialog.Object);
            vm.HasPin = true;

            // Boşluk ve ayraçlar atılır
            vm.PinCode = "12 34";
            Assert.Equal("1234", vm.PinCode);

            // Tam genişlik (Unicode) rakamlar ASCII'ye çevrilir
            vm.PinCode = "１２３４";
            Assert.Equal("1234", vm.PinCode);

            // Arap-Hint rakamları ASCII'ye çevrilir
            vm.PinCode = "١٢٣٤";
            Assert.Equal("1234", vm.PinCode);

            // Karışık içerikte rakam olmayan karakterler atılır
            vm.PinCode = "1-2.3(4)";
            Assert.Equal("1234", vm.PinCode);

            // Onay alanı da aynı kurala uyar
            vm.PinConfirm = "12 ３４";
            Assert.Equal("1234", vm.PinConfirm);

            // Normalleştirme sonrası doğrulama ASCII tabanında çalışır
            vm.TouchField("PinCode");
            vm.TouchField("PinConfirm");
            Assert.Null(vm.PinError);
            Assert.Null(vm.PinConfirmationError);
        }

        [Fact]
        public async Task PinLockout_ConcurrentWrongAttempts_NoLostUpdates()
        {
            // Arrange — aynı profilde 5 EŞZAMANLI yanlış deneme. Eski akışta
            // her deneme ayrı DbContext'te oku-artır-yaz yaptığı için güncellemeler
            // kaybolurdu (UI 5 derken DB 1-2 kalabilirdi).
            var verifier = _pinService.CreateVerifier("1234");
            var profile = await SeedProfileAsync("Concurrent Race", verifier);
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Act — hepsi aynı anda başlar; servis profil bazında serileştirir.
            var tasks = Enumerable.Range(0, 5)
                .Select(_ => service.VerifyAttemptAsync(profile.Id, "9999", verifier))
                .ToArray();
            var results = await Task.WhenAll(tasks);

            // Assert — kayıp güncelleme yok: 5 denemenin TAMAMI sayılmalı,
            // tam olarak biri kilidi tetiklemeli.
            Assert.Equal(5, results.Count(r => !r.IsValid));
            Assert.Equal(1, results.Count(r => r.State.IsLocked));

            using var dbVerify = _contextFactory.CreateDbContext();
            var persisted = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(5, persisted!.FailedPinAttempts);
            Assert.NotNull(persisted.PinLockedUntilUtc);
        }

        [Fact]
        public async Task PinLockout_LockedProfile_RejectsAttemptsUntilExpiry()
        {
            // Arrange — 5 hata ile kilitlenmiş profil
            var verifier = _pinService.CreateVerifier("1234");
            var profile = await SeedProfileAsync("Hard Locked", verifier);
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            for (var i = 0; i < 5; i++)
            {
                await service.VerifyAttemptAsync(profile.Id, "9999", verifier);
            }

            // Act — kilit sırasında DOĞRU PIN bile kabul edilmez; sayaç sıfırlanmaz
            var result = await service.VerifyAttemptAsync(profile.Id, "1234", verifier);

            // Assert — doğrulama reddedilir, kilit sürer
            Assert.False(result.IsValid);
            Assert.True(result.State.IsLocked);
            Assert.True(result.State.RemainingLockDuration!.Value.TotalSeconds is > 20 and <= 30);

            using var dbVerify = _contextFactory.CreateDbContext();
            var persisted = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(5, persisted!.FailedPinAttempts);
            Assert.NotNull(persisted.PinLockedUntilUtc);
        }

        private AddProfileViewModel CreatePinSetupViewModel(
            IProfileService profileService,
            IDialogService dialogService)
        {
            var mockAvatarService = new Mock<IAvatarService>();
            mockAvatarService.Setup(s => s.GetAvatarsByCategory())
                .Returns(new Dictionary<string, List<string>> { { "All", new List<string> { "avatar_1" } } });

            var localization = new Mock<ILocalizationService>();
            localization.Setup(s => s.GetString(It.IsAny<string>())).Returns((string key) => key);

            var vm = new AddProfileViewModel(
                profileService,
                new Mock<IDispatcherService>().Object,
                mockAvatarService.Object,
                dialogService,
                _mockLicenseService.Object,
                new Mock<IM3UParser>().Object,
                new Mock<IXtreamCodesService>().Object,
                new Mock<IStalkerPortalService>().Object,
                _securityService,
                _pinService,
                localization.Object);

            vm.ProfileName = "PIN Profile";
            vm.Url = "http://test.com";
            vm.Username = "test";
            vm.Password = "test";
            return vm;
        }
    }

    /// <summary>
    /// Helper for sharing connection in tests to enable :memory: persistence across contexts
    /// </summary>
    internal class SharedConnectionDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public SharedConnectionDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new AppDbContext(_options);
    }
}
