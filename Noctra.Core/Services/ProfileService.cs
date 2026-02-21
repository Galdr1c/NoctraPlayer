using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class ProfileService : IProfileService
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly IContentDownloadService _contentDownloadService;
    private readonly ILicenseService _licenseService;
    private const string ProfilesLimitKey = "profiles";

    public ProfileService(
        IDbContextFactory<AppDbContext> contextFactory,
        IContentDownloadService contentDownloadService,
        ILicenseService licenseService)
    {
        _contextFactory = contextFactory;
        _contentDownloadService = contentDownloadService;
        _licenseService = licenseService;
    }

    public async Task<Profile?> SaveProfileAsync(ProfileSaveRequest request)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        ProviderAccount account;

        if (request.ExistingIds != null)
        {
            // Update existing provider account
            var existingAccount = await db.ProviderAccounts
                .FirstOrDefaultAsync(a => a.Id == request.ExistingIds.ProviderAccountId);

            if (existingAccount == null)
            {
                throw new InvalidOperationException("ProviderAccount bulunamadı.");
            }

            existingAccount.Url = request.Url;
            existingAccount.Username = request.Username;
            existingAccount.Password = request.EncryptedPassword;
            existingAccount.Type = request.AccountType;
            existingAccount.ExpirationDate = null; // Reset on credential change
            account = existingAccount;
        }
        else
        {
            // Create new provider account
            account = new ProviderAccount
            {
                Name = request.ProfileName + " Hesabı",
                Type = request.AccountType,
                Url = request.Url,
                Username = request.Username,
                Password = request.EncryptedPassword
            };
            db.ProviderAccounts.Add(account);
        }

        Profile profile;

        if (request.ExistingIds != null)
        {
            // Update existing profile
            var existingProfile = await db.Profiles
                .FirstOrDefaultAsync(p => p.Id == request.ExistingIds.ProfileId);

            if (existingProfile == null)
            {
                throw new InvalidOperationException("Profil bulunamadı.");
            }

            existingProfile.Name = request.ProfileName;
            existingProfile.Avatar = request.Avatar;
            existingProfile.IsChild = request.IsChild;
            profile = existingProfile;
        }
        else
        {
            // Check license limit
            var profileCount = await db.Profiles.CountAsync();
            if (!_licenseService.IsWithinLimit(ProfilesLimitKey, profileCount))
            {
                await transaction.RollbackAsync();
                return null; // Signals limit exceeded
            }

            // Create new profile
            profile = new Profile
            {
                Name = request.ProfileName,
                ProviderAccount = account,
                Avatar = request.Avatar,
                IsChild = request.IsChild,
                LastUsed = DateTime.UtcNow
            };
            db.Profiles.Add(profile);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return profile;
    }

    public async Task DeleteProfileAsync(int profileId, int providerAccountId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();

        var hasOtherProfiles = await db.Profiles
            .AnyAsync(p => p.ProviderAccountId == providerAccountId && p.Id != profileId);

        await _contentDownloadService.DeleteProfileDownloadsAsync(profileId);

        await using var transaction = await db.Database.BeginTransactionAsync();

        await db.WatchHistories
            .Where(h => h.ProfileId == profileId)
            .ExecuteDeleteAsync();

        await db.SeriesEpisodeProgresses
            .Where(p => p.ProfileId == profileId)
            .ExecuteDeleteAsync();

        await db.Playlists
            .Where(p => p.ProfileId == profileId)
            .ExecuteDeleteAsync();

        await db.Profiles
            .Where(p => p.Id == profileId)
            .ExecuteDeleteAsync();

        if (!hasOtherProfiles)
        {
            await db.ProviderAccounts
                .Where(a => a.Id == providerAccountId)
                .ExecuteDeleteAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<bool> CheckDuplicateAccountAsync(
        int excludeAccountId, ProfileType type, string url, string username, string password)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.ProviderAccounts
            .AnyAsync(a =>
                a.Id != excludeAccountId &&
                a.Type == type &&
                a.Url == url &&
                (a.Username ?? string.Empty) == username &&
                (a.Password ?? string.Empty) == password);
    }

    public async Task<List<Profile>> GetProfilesAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        return await db.Profiles
            .Include(p => p.ProviderAccount)
            .OrderByDescending(p => p.LastUsed)
            .ToListAsync();
    }

    public async Task UpdateLastUsedAsync(int profileId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(profileId);
        if (profile != null)
        {
            profile.LastUsed = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}
