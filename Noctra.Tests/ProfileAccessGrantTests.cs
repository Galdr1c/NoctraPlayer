using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;
using Noctra.Services.Interfaces;
using Xunit;

namespace Noctra.Tests;

/// <summary>
/// Merkezî profil erişim yetkisi (ProfileAccessGrant) testleri.
/// Korumalı işlemler (yükleme, düzenleme, PIN değiştirme, silme) geçerli bir
/// grant olmadan reddedilmelidir — PIN kapısı View code-behind'e bağlı değildir.
/// </summary>
public sealed class ProfileAccessGrantTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly AppDbContext _context;
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly Mock<IContentDownloadService> _mockDownloadService;
    private readonly Mock<ILicenseService> _mockLicenseService;
    private readonly ProfileAccessService _accessService;

    public ProfileAccessGrantTests()
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
        _mockLicenseService.Setup(l => l.IsPremium).Returns(true);
        _mockLicenseService.Setup(l => l.IsWithinLimit(It.IsAny<string>(), It.IsAny<int>())).Returns(true);
        _accessService = new ProfileAccessService();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Close();
        _connection.Dispose();
    }

    // ── Grant üretimi ─────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquire_NoPin_ReturnsGrantWithoutVerification()
    {
        var profile = new Profile { Id = 1, Name = "No PIN" };

        var grant = await _accessService.TryAcquireAsync(
            profile, ProfileAccessPurpose.Load, (_, _) => throw new InvalidOperationException("Verifier must not run for PIN-less profile"));

        Assert.NotNull(grant);
        Assert.Equal(profile.Id, grant!.ProfileId);
        Assert.Equal(ProfileAccessPurpose.Load, grant.Purpose);
    }

    [Fact]
    public async Task TryAcquire_WithPin_VerifierTrue_ReturnsGrant()
    {
        var profile = new Profile { Id = 2, Name = "PIN", PinHash = "hash" };
        var verified = false;

        var grant = await _accessService.TryAcquireAsync(
            profile, ProfileAccessPurpose.Edit, (p, purpose) =>
            {
                verified = true;
                Assert.Equal(profile.Id, p.Id);
                Assert.Equal(ProfileAccessPurpose.Edit, purpose);
                return Task.FromResult(true);
            });

        Assert.True(verified);
        Assert.NotNull(grant);
    }

    [Fact]
    public async Task TryAcquire_WithPin_VerifierFalse_ReturnsNull()
    {
        var profile = new Profile { Id = 3, Name = "PIN", PinHash = "hash" };

        var grant = await _accessService.TryAcquireAsync(
            profile, ProfileAccessPurpose.Load, (_, _) => Task.FromResult(false));

        Assert.Null(grant);
    }

    [Fact]
    public async Task TryAcquire_WithPin_NoVerifier_ReturnsNull()
    {
        var profile = new Profile { Id = 4, Name = "PIN", PinHash = "hash" };

        var grant = await _accessService.TryAcquireAsync(profile, ProfileAccessPurpose.Load, null!);

        Assert.Null(grant);
    }

    // ── Grant doğrulaması ─────────────────────────────────────────────

    [Fact]
    public void Grant_Expired_IsRejected()
    {
        var grant = new ProfileAccessGrant(1, ProfileAccessPurpose.Load, DateTime.UtcNow.AddSeconds(-1));

        Assert.False(grant.Authorizes(1, ProfileAccessPurpose.Load));
        Assert.Throws<ProfileAccessDeniedException>(() =>
            _accessService.ValidateOrThrow(grant, 1, ProfileAccessPurpose.Load));
    }

    [Fact]
    public void Grant_WrongProfile_IsRejected()
    {
        var grant = ProfileAccessGrant.Create(1, ProfileAccessPurpose.Load);

        Assert.False(grant.Authorizes(2, ProfileAccessPurpose.Load));
        Assert.Throws<ProfileAccessDeniedException>(() =>
            _accessService.ValidateOrThrow(grant, 2, ProfileAccessPurpose.Load));
    }

    [Fact]
    public void Grant_WrongPurpose_IsRejected()
    {
        var grant = ProfileAccessGrant.Create(1, ProfileAccessPurpose.Load);

        Assert.False(grant.Authorizes(1, ProfileAccessPurpose.Delete));
        Assert.Throws<ProfileAccessDeniedException>(() =>
            _accessService.ValidateOrThrow(grant, 1, ProfileAccessPurpose.Delete));
    }

    [Fact]
    public void Grant_Null_IsRejected()
    {
        Assert.Throws<ProfileAccessDeniedException>(() =>
            _accessService.ValidateOrThrow(null, 1, ProfileAccessPurpose.Edit));
    }

    [Fact]
    public void EditGrant_CoversDeleteAndPinChange_ButNotLoad()
    {
        var grant = ProfileAccessGrant.Create(1, ProfileAccessPurpose.Edit);

        Assert.True(grant.Authorizes(1, ProfileAccessPurpose.Edit));
        Assert.True(grant.Authorizes(1, ProfileAccessPurpose.Delete));
        Assert.True(grant.Authorizes(1, ProfileAccessPurpose.PinChange));
        Assert.False(grant.Authorizes(1, ProfileAccessPurpose.Load));
    }

    // ── Servis katmanı koruması ───────────────────────────────────────

    [Fact]
    public async Task DeleteProfile_WithoutGrant_Throws()
    {
        var profile = await SeedProfileAsync("To Delete", _securityService().HashPin("1234"));

        var service = CreateService();

        await Assert.ThrowsAsync<ProfileAccessDeniedException>(() =>
            service.DeleteProfileAsync(profile.Id, profile.ProviderAccountId, null!));

        using var dbVerify = _contextFactory.CreateDbContext();
        Assert.NotNull(await dbVerify.Profiles.FindAsync(profile.Id));
    }

    [Fact]
    public async Task DeleteProfile_WithWrongPurposeGrant_Throws()
    {
        var profile = await SeedProfileAsync("To Delete 2", _securityService().HashPin("1234"));
        var service = CreateService();
        var loadGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Load);

        await Assert.ThrowsAsync<ProfileAccessDeniedException>(() =>
            service.DeleteProfileAsync(profile.Id, profile.ProviderAccountId, loadGrant));
    }

    [Fact]
    public async Task DeleteProfile_WithGrant_Succeeds()
    {
        var profile = await SeedProfileAsync("To Delete 3", _securityService().HashPin("1234"));
        var service = CreateService();
        var grant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Delete);

        await service.DeleteProfileAsync(profile.Id, profile.ProviderAccountId, grant);

        using var dbVerify = _contextFactory.CreateDbContext();
        Assert.Null(await dbVerify.Profiles.FindAsync(profile.Id));
    }

    [Fact]
    public async Task SaveProfile_PinProtectedEdit_WithoutGrant_Throws()
    {
        var profile = await SeedProfileAsync("Edit No Grant", _securityService().HashPin("1234"));
        var service = CreateService();

        var request = new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(profile.Id, profile.ProviderAccountId),
            ProfileName = "Renamed",
            Avatar = "default",
            IsChild = false,
            Url = "http://test.com",
            Username = "test",
            EncryptedPassword = "test",
            AccountType = ProfileType.M3U,
            PinHash = _securityService().HashPin("1234") // same hash — only rename
        };

        await Assert.ThrowsAsync<ProfileAccessDeniedException>(() =>
            service.SaveProfileAsync(request));

        using var dbVerify = _contextFactory.CreateDbContext();
        Assert.Equal("Edit No Grant", (await dbVerify.Profiles.FindAsync(profile.Id))!.Name);
    }

    [Fact]
    public async Task SaveProfile_PinChange_WithEditGrant_Succeeds()
    {
        var profile = await SeedProfileAsync("Edit With Edit Grant", _securityService().HashPin("1234"));
        var service = CreateService();
        var newHash = _securityService().HashPin("9999");

        var request = new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(profile.Id, profile.ProviderAccountId),
            ProfileName = "Renamed",
            Avatar = "default",
            IsChild = false,
            Url = "http://test.com",
            Username = "test",
            EncryptedPassword = "test",
            AccountType = ProfileType.M3U,
            PinHash = newHash,
            AccessGrant = ProfileAccessGrant.Create(profile.Id, ProfileAccessPurpose.Edit)
        };

        await service.SaveProfileAsync(request);

        using var dbVerify = _contextFactory.CreateDbContext();
        var updated = await dbVerify.Profiles.FindAsync(profile.Id);
        Assert.Equal(newHash, updated!.PinHash);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task SaveProfile_PinlessEdit_WithoutGrant_Succeeds()
    {
        var profile = await SeedProfileAsync("Pinless Edit", null);
        var service = CreateService();

        var request = new ProfileSaveRequest
        {
            ExistingIds = new ExistingProfileIds(profile.Id, profile.ProviderAccountId),
            ProfileName = "Renamed Pinless",
            Avatar = "default",
            IsChild = false,
            Url = "http://test.com",
            Username = "test",
            EncryptedPassword = "test",
            AccountType = ProfileType.M3U,
            PinHash = null
        };

        var saved = await service.SaveProfileAsync(request);

        Assert.NotNull(saved);
        Assert.Equal("Renamed Pinless", saved!.Name);
    }

    [Fact]
    public void Grant_IsValidFor_MatchingProfileAndPurpose()
    {
        var grant = ProfileAccessGrant.Create(1, ProfileAccessPurpose.Load);

        Assert.True(grant.Authorizes(1, ProfileAccessPurpose.Load));
        Assert.False(grant.Authorizes(1, ProfileAccessPurpose.Edit));
        Assert.False(grant.Authorizes(2, ProfileAccessPurpose.Load));
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private SecurityService _securityService() => new();

    private async Task<Profile> SeedProfileAsync(string name, string? pinHash)
    {
        var account = new ProviderAccount { Name = name + " Account", Url = "http://test.com", Type = ProfileType.M3U };
        var profile = new Profile
        {
            Name = name,
            ProviderAccount = account,
            Avatar = "default",
            PinHash = pinHash
        };
        _context.ProviderAccounts.Add(account);
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();
        return profile;
    }

    private ProfileService CreateService()
        => new(_contextFactory, _mockDownloadService.Object, _mockLicenseService.Object, profileAccessService: _accessService);
}
