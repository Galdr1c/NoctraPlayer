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
    private readonly ISettingsService? _settingsService;
    private const string ProfilesLimitKey = "profiles";

    public ProfileService(
        IDbContextFactory<AppDbContext> contextFactory,
        IContentDownloadService contentDownloadService,
        ILicenseService licenseService,
        ISettingsService? settingsService = null)
    {
        _contextFactory = contextFactory;
        _contentDownloadService = contentDownloadService;
        _licenseService = licenseService;
        _settingsService = settingsService;
    }

    public async Task<Profile?> SaveProfileAsync(ProfileSaveRequest request)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        ProviderAccount account;
        bool credentialsChanged = false;

        if (request.ExistingIds != null)
        {
            // Update existing provider account
            var existingAccount = await db.ProviderAccounts
                .FirstOrDefaultAsync(a => a.Id == request.ExistingIds.ProviderAccountId);

            if (existingAccount == null)
            {
                throw new InvalidOperationException("ProviderAccount bulunamadı.");
            }

            // CredentialsChanged is now calculated reliably in AddProfileViewModel
            if (request.CredentialsChanged)
            {
                credentialsChanged = true;
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
            // existingProfile.IsChild is intentionally NOT updated here. 
            // A child profile cannot be unchecked, and a regular profile cannot be made a child profile later.
            existingProfile.PinHash = request.PinHash;
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
                PinHash = request.PinHash,
                LastUsed = DateTime.UtcNow
            };
            db.Profiles.Add(profile);
        }

        await db.SaveChangesAsync();

        if (credentialsChanged)
        {
            // If credentials changed, delete associated playlists to force a complete re-sync
            // but keep SeriesEpisodeProgresses (they are profile-wide)
            await db.Playlists
                .Where(pl => pl.ProfileId == profile.Id)
                .ExecuteDeleteAsync();

            // Phase 28: Fail active downloads for this profile because credentials changed
            // and the source URLs/tokens may no longer be valid.
            await _contentDownloadService.FailActiveDownloadsForProfileAsync(
                profile.Id, 
                "Hesap bilgileri degistirildi. Indirmeyi yeni bilgilerle bastan baslatmaniz gerekiyor.");
        }
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

        await CleanDeletedProfileSettingsAsync();
    }

    public async Task<bool> CheckDuplicateAccountAsync(
        int excludeAccountId, ProfileType type, string url, string username, string password)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        
        // Normalize: Treat null as empty string for comparison
        var normalizedUrl = url ?? string.Empty;
        var normalizedUsername = username ?? string.Empty;

        return await db.ProviderAccounts
            .AnyAsync(a =>
                a.Id != excludeAccountId &&
                a.Type == type &&
                a.Url == normalizedUrl &&
                (a.Username ?? string.Empty) == normalizedUsername);
    }

    public async Task<List<Profile>> GetProfilesAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var profiles = await db.Profiles
            .Include(p => p.ProviderAccount)
            .OrderByDescending(p => p.LastUsed)
            .ToListAsync();

        await CleanDeletedProfileSettingsAsync(profiles.Select(p => p.Id));
        return profiles;
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

    public async Task ScheduleProfileDeletionAsync(int profileId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(profileId);
        if (profile == null) return;

        if (profile.PendingDeletionAt != null) return;

        profile.PendingDeletionAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task CancelProfileDeletionAsync(int profileId)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var profile = await db.Profiles.FindAsync(profileId);
        if (profile == null) return;

        profile.PendingDeletionAt = null;
        await db.SaveChangesAsync();
    }

    public async Task PurgeExpiredProfilesAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-3);

        var expired = await db.Profiles
            .Include(p => p.ProviderAccount)
            .Where(p => p.PendingDeletionAt.HasValue && p.PendingDeletionAt <= cutoff)
            .ToListAsync();

        foreach (var profile in expired)
        {
            await _contentDownloadService.DeleteProfileDownloadsAsync(profile.Id);

            var hasOtherProfiles = await db.Profiles
                .AnyAsync(p => p.ProviderAccountId == profile.ProviderAccountId && p.Id != profile.Id);

            await db.WatchHistories
                .Where(h => h.ProfileId == profile.Id)
                .ExecuteDeleteAsync();

            await db.SeriesEpisodeProgresses
                .Where(p => p.ProfileId == profile.Id)
                .ExecuteDeleteAsync();

            await db.Playlists
                .Where(p => p.ProfileId == profile.Id)
                .ExecuteDeleteAsync();

            db.Profiles.Remove(profile);

            if (!hasOtherProfiles && profile.ProviderAccount != null)
            {
                db.ProviderAccounts.Remove(profile.ProviderAccount);
            }
        }

        if (expired.Any())
        {
            await db.SaveChangesAsync();
            await CleanDeletedProfileSettingsAsync();
        }
    }

    private async Task CleanDeletedProfileSettingsAsync(IEnumerable<int>? activeProfileIds = null)
    {
        if (_settingsService == null)
        {
            return;
        }

        if (activeProfileIds == null)
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            activeProfileIds = await db.Profiles
                .Select(p => p.Id)
                .ToListAsync();
        }

        await _settingsService.CleanOrphanedSettingsAsync(activeProfileIds);
    }
}
