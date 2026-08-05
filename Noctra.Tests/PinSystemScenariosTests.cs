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
            Assert.True(_securityService.VerifyPin(pin, profile.PinHash));
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

            // Act — non-premium user edits profile details only
            vm.ProfileName = "Renamed Profile";
            await vm.SaveCommand.ExecuteAsync(null);

            // Assert — the existing PIN must be preserved, not silently removed
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.NotNull(updatedProfile);
            Assert.Equal(oldHash, updatedProfile!.PinHash);
            Assert.True(_securityService.VerifyPin(oldPin, updatedProfile.PinHash));
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
                PinHash = newHash
            };

            // Act
            await service.SaveProfileAsync(request);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Equal(newHash, updatedProfile!.PinHash);
            Assert.True(_securityService.VerifyPin(newPin, updatedProfile.PinHash));
            Assert.False(_securityService.VerifyPin(oldPin, updatedProfile.PinHash));
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
                PinHash = null // Explicitly remove
            };

            // Act
            await service.SaveProfileAsync(request);

            // Assert
            using var dbVerify = _contextFactory.CreateDbContext();
            var updatedProfile = await dbVerify.Profiles.FindAsync(profile.Id);
            Assert.Null(updatedProfile!.PinHash);
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
