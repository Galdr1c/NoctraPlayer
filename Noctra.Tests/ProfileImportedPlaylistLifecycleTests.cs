using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Core.Services;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

public sealed class ProfileImportedPlaylistLifecycleTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _importsDirectory;
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Mock<IContentDownloadService> _downloadService = new();
    private readonly Mock<ILicenseService> _licenseService = new();

    public ProfileImportedPlaylistLifecycleTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "Noctra-ImportedPlaylistLifecycle",
            Guid.NewGuid().ToString("N"));
        _importsDirectory = Path.Combine(_testRoot, "Imports");
        Directory.CreateDirectory(_importsDirectory);

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new AppDbContext(options);
        _context.Database.EnsureCreated();
        _contextFactory = new SharedConnectionDbContextFactory(options);

        _licenseService
            .Setup(service => service.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(true);
        _downloadService
            .Setup(service => service.DeleteProfileDownloadsAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _downloadService
            .Setup(service => service.FailActiveDownloadsForProfileAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task DeleteProfileAsync_DeletesUnreferencedManagedImportCopy()
    {
        var managedCopy = CreateFile(_importsDirectory, "delete-me.m3u");
        var profile = await SeedProfileAsync("Delete", managedCopy);
        var service = CreateService();

        await service.DeleteProfileAsync(
            profile.Id,
            profile.ProviderAccountId,
            ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Delete));

        Assert.False(File.Exists(managedCopy));
    }

    [Fact]
    public async Task DeleteProfileAsync_PreservesFileOutsideManagedImportsDirectory()
    {
        var externalDirectory = Path.Combine(_testRoot, "External");
        var externalFile = CreateFile(externalDirectory, "user-owned.m3u");
        var profile = await SeedProfileAsync("External", externalFile);
        var service = CreateService();

        await service.DeleteProfileAsync(
            profile.Id,
            profile.ProviderAccountId,
            ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Delete));

        Assert.True(File.Exists(externalFile));
    }

    [Fact]
    public async Task DeleteProfileAsync_PreservesManagedCopyReferencedByAnotherProfile()
    {
        var sharedCopy = CreateFile(_importsDirectory, "shared.m3u");
        var deletedProfile = await SeedProfileAsync("Deleted", sharedCopy);
        var survivingProfile = await SeedProfileAsync("Surviving", sharedCopy);
        var service = CreateService();

        await service.DeleteProfileAsync(
            deletedProfile.Id,
            deletedProfile.ProviderAccountId,
            ProfileAccessGrant.Create(deletedProfile.Id, ProfileAccessPurpose.Delete));

        Assert.True(File.Exists(sharedCopy));
        await using var verify = await _contextFactory.CreateDbContextAsync();
        Assert.NotNull(await verify.Profiles.FindAsync(survivingProfile.Id));
    }

    [Fact]
    public async Task SaveProfileAsync_WhenLocalSourceChanges_DeletesOnlyOldManagedCopy()
    {
        var oldCopy = CreateFile(_importsDirectory, "old.m3u");
        var newCopy = CreateFile(_importsDirectory, "new.m3u");
        var profile = await SeedProfileAsync("Edited", oldCopy);
        var service = CreateService();

        var saved = await service.SaveProfileAsync(new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(profile.Id, profile.ProviderAccountId),
            ProfileName = profile.Name,
            Avatar = profile.Avatar,
            Url = newCopy,
            Username = string.Empty,
            EncryptedPassword = string.Empty,
            AccountType = ProfileType.M3U,
            CredentialsChanged = true
        });

        Assert.NotNull(saved);
        Assert.False(File.Exists(oldCopy));
        Assert.True(File.Exists(newCopy));
    }

    [Fact]
    public async Task SaveProfileAsync_MetadataOnlyEdit_DoesNotRequireMissingManagedSource()
    {
        var managedCopy = CreateFile(_importsDirectory, "externally-removed.m3u");
        var profile = await SeedProfileAsync("Metadata", managedCopy);
        File.Delete(managedCopy);
        var service = CreateService();

        var saved = await service.SaveProfileAsync(new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(profile.Id, profile.ProviderAccountId),
            ProfileName = "Renamed Metadata",
            Avatar = profile.Avatar,
            Url = managedCopy,
            Username = string.Empty,
            EncryptedPassword = string.Empty,
            AccountType = ProfileType.M3U,
            CredentialsChanged = false
        });

        Assert.NotNull(saved);
        Assert.Equal("Renamed Metadata", saved!.Name);
    }

    [Fact]
    public async Task PurgeExpiredProfilesAsync_DeletesManagedImportCopyAfterCommit()
    {
        var managedCopy = CreateFile(_importsDirectory, "expired.m3u");
        await SeedProfileAsync(
            "Expired",
            managedCopy,
            pendingDeletionAt: DateTime.UtcNow.AddDays(-4));
        var service = CreateService();

        await service.PurgeExpiredProfilesAsync();

        Assert.False(File.Exists(managedCopy));
    }

    [Fact]
    public async Task DeleteChildProfilesAsync_DeletesManagedImportCopyAfterCommit()
    {
        var managedCopy = CreateFile(_importsDirectory, "legacy-child.m3u");
        await SeedProfileAsync("Legacy Child", managedCopy, isChild: true);
        var service = CreateService();

        var deletedCount = await service.DeleteChildProfilesAsync();

        Assert.Equal(1, deletedCount);
        Assert.False(File.Exists(managedCopy));
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenImportsPathResolutionFails_DoesNotReportCommittedDeleteAsFailed()
    {
        var managedCopy = CreateFile(_importsDirectory, "invalid-root.m3u");
        var profile = await SeedProfileAsync("Invalid Root", managedCopy);
        var service = CreateService(new ThrowingAppPathService());

        await service.DeleteProfileAsync(
            profile.Id,
            profile.ProviderAccountId,
            ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Delete));

        await using var verify = await _contextFactory.CreateDbContextAsync();
        Assert.Null(await verify.Profiles.FindAsync(profile.Id));
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenOneManagedCopyIsLocked_StillDeletesOtherCopy()
    {
        var lockedCopy = CreateFile(_importsDirectory, "locked.m3u");
        var deletableCopy = CreateFile(_importsDirectory, "deletable.m3u");
        var profile = await SeedProfileAsync("Multiple", lockedCopy);
        _context.Playlists.Add(new Playlist
        {
            Name = "Deletable Playlist",
            FilePath = deletableCopy,
            ProfileId = profile.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await using var lockStream = new FileStream(
            lockedCopy,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);
        var service = CreateService();

        await service.DeleteProfileAsync(
            profile.Id,
            profile.ProviderAccountId,
            ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Delete));

        Assert.True(File.Exists(lockedCopy));
        Assert.False(File.Exists(deletableCopy));
    }

    [Fact]
    public async Task ProfileLifecycleOperations_SerializeConcurrentDeleteAndManagedSourceCreation()
    {
        var candidate = CreateFile(_importsDirectory, "concurrent.m3u");
        var deletingProfile = await SeedProfileAsync("Deleting", candidate);
        var existingSource = CreateFile(_importsDirectory, "existing.m3u");
        var editedProfile = await SeedProfileAsync("Edited Concurrently", existingSource);
        var deleteEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDelete = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _downloadService
            .Setup(service => service.DeleteProfileDownloadsAsync(
                deletingProfile.Id,
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                deleteEntered.TrySetResult();
                await releaseDelete.Task;
            });
        var service = CreateService();

        var deleteTask = service.DeleteProfileAsync(
            deletingProfile.Id,
            deletingProfile.ProviderAccountId,
            ProfileAccessGrant.Create(deletingProfile.Id, ProfileAccessPurpose.Delete));
        await deleteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var saveTask = service.SaveProfileAsync(new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(
                editedProfile.Id,
                editedProfile.ProviderAccountId),
            ProfileName = editedProfile.Name,
            Avatar = editedProfile.Avatar,
            Url = candidate,
            Username = string.Empty,
            EncryptedPassword = string.Empty,
            AccountType = ProfileType.M3U,
            CredentialsChanged = true
        });

        try
        {
            await Task.Delay(250);
            Assert.False(saveTask.IsCompleted);
        }
        finally
        {
            releaseDelete.TrySetResult();
        }

        await deleteTask;
        await Assert.ThrowsAsync<FileNotFoundException>(() => saveTask);

        await using var verify = await _contextFactory.CreateDbContextAsync();
        var account = await verify.ProviderAccounts.FindAsync(editedProfile.ProviderAccountId);
        Assert.Equal(existingSource, account!.Url);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();

        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Test teardown must not hide the assertion result.
        }
    }

    private ProfileService CreateService(
        IAppPathService? appPathService = null,
        ILogger<ProfileService>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_contextFactory);
        services.AddSingleton(_downloadService.Object);
        services.AddSingleton(_licenseService.Object);
        services.AddSingleton<IAppPathService>(
            appPathService ?? new DesktopAppPathService(_testRoot, _testRoot));
        if (logger is not null)
        {
            services.AddSingleton(logger);
        }

        using var provider = services.BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<ProfileService>(provider);
    }

    private async Task<Profile> SeedProfileAsync(
        string name,
        string sourcePath,
        DateTime? pendingDeletionAt = null,
        bool isChild = false)
    {
        var account = new ProviderAccount
        {
            Name = name + " Account",
            Url = sourcePath,
            Type = ProfileType.M3U
        };
        var profile = new Profile
        {
            Name = name,
            Avatar = "default",
            ProviderAccount = account,
            LastUsed = DateTime.UtcNow,
            PendingDeletionAt = pendingDeletionAt,
            IsChild = isChild
        };

        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();

        _context.Playlists.Add(new Playlist
        {
            Name = name + " Playlist",
            FilePath = sourcePath,
            ProfileId = profile.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        return profile;
    }

    private static string CreateFile(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "#EXTM3U");
        return path;
    }

    private sealed class ThrowingAppPathService : IAppPathService
    {
        public string UserDataDirectory => throw new IOException("Invalid test path.");
        public string SettingsDirectory => string.Empty;
        public string DownloadsDirectory => string.Empty;
        public string LegacyDownloadsDirectory => string.Empty;
        public string DatabasePath => string.Empty;
        public string LegacyDatabasePath => string.Empty;
        public string TempPlaybackDirectory => string.Empty;
        public string LogsDirectory => string.Empty;

        public void EnsureUserDataDirectory()
        {
        }

        public string NormalizeDownloadDirectory(string? path) => path ?? string.Empty;
    }
}
