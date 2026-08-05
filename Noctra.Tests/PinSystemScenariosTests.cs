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
        public async Task PinCreation_SavesCorrectHash_WhenValid()
        {
            // Arrange
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            var pin = "1234";
            var expectedHash = _securityService.HashPin(pin);

            var request = new ProfileSaveRequest
            {
                ProfileName = "PIN Profile",
                Avatar = "default",
                IsChild = false,
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
            Assert.Equal(PinVerificationResult.Valid, _securityService.VerifyPin(pin, profile.PinHash));
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
            var oldHash = _securityService.HashPin(oldPin);
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
            Assert.Equal(PinVerificationResult.Valid, _securityService.VerifyPin(oldPin, updatedProfile.PinHash));
        }

        [Fact]
        public async Task PinManagement_ChangePin_UpdatesHash()
        {
            // Arrange
            var oldPin = "1111";
            var oldHash = _securityService.HashPin(oldPin);
            var profile = await SeedProfileAsync("Old PIN", oldHash);
            
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);
            
            var newPin = "9999";
            var newHash = _securityService.HashPin(newPin);

            var request = new ProfileSaveRequest
            {
                ExistingIds = new ExistingProfileIds ( profile.Id, profile.ProviderAccountId ),
                ProfileName = "New PIN",
                Avatar = "default",
                IsChild = false,
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
            Assert.Equal(PinVerificationResult.Valid, _securityService.VerifyPin(newPin, updatedProfile.PinHash));
            Assert.Equal(PinVerificationResult.Invalid, _securityService.VerifyPin(oldPin, updatedProfile.PinHash));
        }

        [Fact]
        public async Task PinManagement_RemovePin_SetsHashToNull()
        {
            // Arrange
            var profile = await SeedProfileAsync("PIN to Remove", _securityService.HashPin("1234"));
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            var request = new ProfileSaveRequest
            {
                ExistingIds = new ExistingProfileIds ( profile.Id, profile.ProviderAccountId ),
                ProfileName = "No PIN",
                Avatar = "default",
                IsChild = false,
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
            var profile = await SeedProfileAsync("Locked", _securityService.HashPin("1234"));
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
            var profile = await SeedProfileAsync("Reset", _securityService.HashPin("1234"));
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
            var profile = await SeedProfileAsync("Expired Lock", _securityService.HashPin("1234"));
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
                _securityService,
                new Mock<IDispatcherService>().Object,
                _securityService.HashPin("1234"),
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
        public void PinEntry_LockoutCountdownText_UsesLocalizedFormat()
        {
            // Arrange — Türkçe format; saniye birimi metnin parçası
            var localization = new Mock<ILocalizationService>();
            localization
                .Setup(service => service.GetString("PinEntry.LockoutCountdownFormat"))
                .Returns("Kilitli. {0} saniye sonra tekrar deneyin.");

            var vm = new PinEntryViewModel(
                _securityService,
                new Mock<IDispatcherService>().Object,
                _securityService.HashPin("1234"),
                "Test",
                string.Empty,
                "Login",
                localization.Object,
                failedAttempts: ProfileService.MaxPinAttempts,
                lockedUntilUtc: DateTime.UtcNow.AddSeconds(30));

            using (vm)
            {
                // Act
                var text = vm.LockoutCountdownText;

                // Assert — sayı formata gömülü; ham "s" eki ya da eski anahtar yok
                Assert.Contains(vm.LockSecondsRemaining.ToString(), text);
                Assert.Contains("saniye sonra tekrar deneyin", text);
                Assert.DoesNotContain("Kilitli. Kalan süre", text);
            }
        }

        [Fact]
        public async Task PinEntry_Dispose_CancelsLockoutCountdown()
        {
            // Arrange
            var dispatcher = new Mock<IDispatcherService>();
            var vm = new PinEntryViewModel(
                _securityService,
                dispatcher.Object,
                _securityService.HashPin("1234"),
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
                _securityService,
                dispatcher.Object,
                _securityService.HashPin("1234"),
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
            // Arrange
            var localization = new Mock<ILocalizationService>();
            localization.Setup(s => s.GetString(It.IsAny<string>())).Returns((string key) => key);

            var vm = new PinEntryViewModel(
                _securityService,
                new Mock<IDispatcherService>().Object,
                _securityService.HashPin("1234"),
                "Test",
                string.Empty,
                "Login",
                localization.Object,
                failedAttempts: 4);

            var failed = 0;
            vm.AttemptFailed += (_, count) => failed = count;

            // Act — one wrong entry after 4 persisted failures reaches the threshold
            foreach (var digit in "1111")
            {
                await vm.PressDigitCommand.ExecuteAsync(digit.ToString());
            }

            // Assert — the 5th failure is reported so the caller can persist the lock
            Assert.Equal(5, failed);
            Assert.Equal("PinEntry.Error.TooManyAttempts", vm.ErrorMessage);
        }

        [Fact]
        public async Task PinEntry_VerificationBlocked_WhileIsVerifying()
        {
            // Arrange
            var vm = new PinEntryViewModel(
                _securityService,
                new Mock<IDispatcherService>().Object,
                _securityService.HashPin("1234"),
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object);

            // Act — 4 digits start async verification
            var verificationTask = vm.PressDigitCommand.ExecuteAsync("1234");

            // Assert — verification runs in the background, keypad is disabled meanwhile
            Assert.True(vm.IsVerifying);
            Assert.False(vm.IsKeypadEnabled);

            // Input is ignored while verifying (correct PIN keeps the digits visible)
            vm.PressDigitCommand.Execute("9");
            Assert.Equal("1234", vm.EnteredPin);

            await verificationTask;

            Assert.False(vm.IsVerifying);
            Assert.True(vm.IsKeypadEnabled);
        }

        [Fact]
        public async Task PinEntry_LegacyHash_VerifiesAndRaisesNeedsRehash()
        {
            // Arrange
            const string legacyHashFor1234 = "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88";
            var vm = new PinEntryViewModel(
                _securityService,
                new Mock<IDispatcherService>().Object,
                legacyHashFor1234,
                "Test",
                string.Empty,
                "Login",
                new Mock<ILocalizationService>().Object);

            bool? result = null;
            string? rehashedPin = null;
            vm.PinResult += (_, value) => result = value;
            vm.PinNeedsRehash += (_, pin) => rehashedPin = pin;

            // Act
            foreach (var digit in "1234")
            {
                await vm.PressDigitCommand.ExecuteAsync(digit.ToString());
            }

            // Assert — legacy hash still unlocks but requests a rehash of the same PIN
            Assert.True(result);
            Assert.Equal("1234", rehashedPin);
        }

        [Fact]
        public async Task UpgradePinHashAsync_LegacyHash_IsReplacedWithPbkdf2()
        {
            // Arrange
            const string legacyHashFor1234 = "83D837DD7E939316F5A94A1216FF2E6F2DC9E9859441F333CC12FA2414468B88";
            var profile = await SeedProfileAsync("Legacy PIN", legacyHashFor1234);
            var service = new ProfileService(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object);

            // Act — full upgrade path: verify legacy → rehash → persist
            Assert.Equal(
                PinVerificationResult.ValidNeedsRehash,
                _securityService.VerifyPin("1234", profile.PinHash));

            var newHash = _securityService.HashPin("1234");
            await service.UpgradePinHashAsync(profile.Id, newHash);

            // Assert — same PIN now verifies as current-format hash
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile);
            Assert.Equal(newHash, updatedProfile!.PinHash);
            Assert.Equal(
                PinVerificationResult.Valid,
                _securityService.VerifyPin("1234", updatedProfile.PinHash));
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
            var profile = await SeedProfileAsync("Doomed Profile", _securityService.HashPin("1234"));
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
            var profile = await SeedProfileAsync("Existing PIN", _securityService.HashPin("1234"));
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
