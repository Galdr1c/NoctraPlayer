using System.Collections.Concurrent;
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
    private readonly IProfileAccessService _profileAccessService;
    private readonly IProfilePinService _pinService;
    private const string ProfilesLimitKey = "profiles";

    public const int MaxPinAttempts = 5;
    public const int PinLockoutDurationSeconds = 30;

    /// <summary>
    /// Profil bazlı PIN deneme senkronizasyonu. PIN doğrulama, deneme sayacı
    /// ve kilit işlemleri aynı profil için serileştirilir — böylece eşzamanlı
    /// (veya hızlı art arda) denemeler sayaçtaki güncellemeleri kaybettiremez.
    /// Profil sayısı küçük olduğundan girişler yaşam süresi boyunca tutulur.
    /// </summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _pinSemaphores = new();

    public ProfileService(
        IDbContextFactory<AppDbContext> contextFactory,
        IContentDownloadService contentDownloadService,
        ILicenseService licenseService,
        ISettingsService? settingsService = null,
        IProfileAccessService? profileAccessService = null,
        IProfilePinService? pinService = null)
    {
        _contextFactory = contextFactory;
        _contentDownloadService = contentDownloadService;
        _licenseService = licenseService;
        _settingsService = settingsService;
        _profileAccessService = profileAccessService ?? new ProfileAccessService();
        _pinService = pinService ?? new ProfilePinService();
    }

    private SemaphoreSlim GetProfilePinSemaphore(int profileId) =>
        _pinSemaphores.GetOrAdd(profileId, static _ => new SemaphoreSlim(1, 1));

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

            // PIN korumalı bir profilin düzenlenmesi merkezî bir erişim yetkisi
            // ister — PIN kapısı yalnızca View code-behind'de değil, servis
            // katmanında da zorunludur. Deep link, kısayol veya yanlış bağlanmış
            // bir komut bu kapıyı atlayamaz.
            if (!string.IsNullOrEmpty(existingProfile.PinHash))
            {
                var pinChanged = !string.Equals(
                    existingProfile.PinHash, request.PinHash, StringComparison.Ordinal);
                _profileAccessService.ValidateOrThrow(
                    request.AccessGrant,
                    existingProfile.Id,
                    pinChanged ? ProfileAccessPurpose.PinChange : ProfileAccessPurpose.Edit);
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
            await DeleteProfilePlaylistContentAsync(db, profile.Id);

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

    private static async Task DeleteProfilePlaylistContentAsync(AppDbContext db, int profileId)
    {
        var playlistIds = await db.Playlists
            .Where(pl => pl.ProfileId == profileId)
            .Select(pl => pl.Id)
            .ToListAsync();

        if (playlistIds.Count == 0)
        {
            return;
        }

        var epgChannels = await db.Channels
            .Where(c => playlistIds.Contains(c.PlaylistId))
            .Select(c => new { c.TvgId, c.TvgName, c.Name })
            .ToListAsync();

        var epgChannelIds = epgChannels
            .SelectMany(c => new[] { c.TvgId, c.TvgName, c.Name })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (epgChannelIds.Count > 0)
        {
            const int batchSize = 500;
            for (var i = 0; i < epgChannelIds.Count; i += batchSize)
            {
                var batch = epgChannelIds.Skip(i).Take(batchSize).ToList();
                await db.EpgPrograms
                    .Where(e => batch.Contains(e.ChannelId))
                    .ExecuteDeleteAsync();
            }
        }

        await db.Series
            .Where(s => playlistIds.Contains(s.PlaylistId))
            .ExecuteDeleteAsync();
    }

    public async Task DeleteProfileAsync(int profileId, int providerAccountId, ProfileAccessGrant grant)
    {
        _profileAccessService.ValidateOrThrow(grant, profileId, ProfileAccessPurpose.Delete);

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

    public async Task<PinVerificationState> GetPinVerificationStateAsync(int profileId)
    {
        var semaphore = GetProfilePinSemaphore(profileId);
        await semaphore.WaitAsync();
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var profile = await db.Profiles.FindAsync(profileId);
            if (profile == null)
            {
                return new PinVerificationState(0, null, false, null);
            }

            var now = DateTime.UtcNow;
            if (profile.PinLockedUntilUtc is { } until && until <= now)
            {
                profile.FailedPinAttempts = 0;
                profile.PinLockedUntilUtc = null;
                await db.SaveChangesAsync();
                return new PinVerificationState(0, null, false, null);
            }

            return ToPinVerificationState(profile, now);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ProfilePinAttemptResult> VerifyAttemptAsync(
        int profileId,
        string pin,
        string verifier)
    {
        var semaphore = GetProfilePinSemaphore(profileId);
        await semaphore.WaitAsync();
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var profile = await db.Profiles.FindAsync(profileId);
            if (profile == null)
            {
                return new ProfilePinAttemptResult(
                    false,
                    new PinVerificationState(0, null, false, null));
            }

            var now = DateTime.UtcNow;

            // Süresi dolmuş kilit — sayaç sıfırlanır, kilit temizlenir.
            if (profile.PinLockedUntilUtc is { } until && until <= now)
            {
                profile.FailedPinAttempts = 0;
                profile.PinLockedUntilUtc = null;
            }

            // Aktif kilit — doğrulamaya girilmez, güncel kilit durumu döner.
            if (profile.PinLockedUntilUtc is { } activeUntil && activeUntil > now)
            {
                return new ProfilePinAttemptResult(
                    false,
                    ToPinVerificationState(profile, now));
            }

            // Savunma: PIN'siz (boş verifier) profil için sayaç kirletilmez —
            // UI bu profillerde kapıyı hiç açmaz; servis yalnızca bağımsız
            // olarak da güvenli davranır (sayacı artırmadan reddeder).
            if (string.IsNullOrWhiteSpace(verifier))
            {
                return new ProfilePinAttemptResult(
                    false,
                    ToPinVerificationState(profile, now));
            }

            if (_pinService.Verify(pin, verifier))
            {
                profile.FailedPinAttempts = 0;
                profile.PinLockedUntilUtc = null;
                await db.SaveChangesAsync();
                return new ProfilePinAttemptResult(
                    true,
                    new PinVerificationState(0, null, false, null));
            }

            profile.FailedPinAttempts++;
            if (profile.FailedPinAttempts >= MaxPinAttempts)
            {
                profile.PinLockedUntilUtc = now.AddSeconds(PinLockoutDurationSeconds);
            }

            await db.SaveChangesAsync();
            return new ProfilePinAttemptResult(
                false,
                ToPinVerificationState(profile, now));
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<PinVerificationState> RegisterPinFailureAsync(int profileId)
    {
        var semaphore = GetProfilePinSemaphore(profileId);
        await semaphore.WaitAsync();
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var profile = await db.Profiles.FindAsync(profileId);
            if (profile == null)
            {
                return new PinVerificationState(0, null, false, null);
            }

            var now = DateTime.UtcNow;
            if (profile.PinLockedUntilUtc is { } until && until > now)
            {
                return ToPinVerificationState(profile, now);
            }

            profile.FailedPinAttempts++;
            if (profile.FailedPinAttempts >= MaxPinAttempts)
            {
                profile.PinLockedUntilUtc = now.AddSeconds(PinLockoutDurationSeconds);
            }

            await db.SaveChangesAsync();
            return ToPinVerificationState(profile, now);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task ResetPinAttemptsAsync(int profileId)
    {
        var semaphore = GetProfilePinSemaphore(profileId);
        await semaphore.WaitAsync();
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync();
            var profile = await db.Profiles.FindAsync(profileId);
            if (profile == null)
            {
                return;
            }

            profile.FailedPinAttempts = 0;
            profile.PinLockedUntilUtc = null;
            await db.SaveChangesAsync();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static PinVerificationState ToPinVerificationState(Profile profile, DateTime now)
    {
        if (profile.PinLockedUntilUtc is { } until && until > now)
        {
            return new PinVerificationState(
                profile.FailedPinAttempts,
                until,
                true,
                until - now);
        }

        return new PinVerificationState(
            profile.FailedPinAttempts,
            null,
            false,
            null);
    }
}
