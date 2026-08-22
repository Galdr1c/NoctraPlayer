using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Noctra.Core.Services;
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
    private readonly IAppPathService? _appPathService;
    private readonly ILogger<ProfileService>? _logger;
    private const string ProfilesLimitKey = "profiles";
    private static readonly SemaphoreSlim ImportedPlaylistLifecycleGate = new(1, 1);

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
        IProfilePinService? pinService = null,
        IAppPathService? appPathService = null,
        ILogger<ProfileService>? logger = null)
    {
        _contextFactory = contextFactory;
        _contentDownloadService = contentDownloadService;
        _licenseService = licenseService;
        _settingsService = settingsService;
        _profileAccessService = profileAccessService ?? new ProfileAccessService();
        _pinService = pinService ?? new ProfilePinService();
        _appPathService = appPathService;
        _logger = logger;
    }

    private SemaphoreSlim GetProfilePinSemaphore(int profileId) =>
        _pinSemaphores.GetOrAdd(profileId, static _ => new SemaphoreSlim(1, 1));

    public async Task<Profile?> SaveProfileAsync(ProfileSaveRequest request)
    {
        if (_appPathService is null)
        {
            return await SaveProfileCoreAsync(request);
        }

        await ImportedPlaylistLifecycleGate.WaitAsync();
        try
        {
            if (request.ExistingIds is null || request.CredentialsChanged)
            {
                ValidateManagedImportSourceExists(request);
            }

            return await SaveProfileCoreAsync(request);
        }
        finally
        {
            ImportedPlaylistLifecycleGate.Release();
        }
    }

    private async Task<Profile?> SaveProfileCoreAsync(ProfileSaveRequest request)
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        ProviderAccount account;
        bool credentialsChanged = false;
        var importedPlaylistCandidates = new List<string>();

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
                importedPlaylistCandidates.AddRange(
                    await CollectProfileImportedPlaylistCandidatesAsync(
                        db,
                        [request.ExistingIds.ProfileId]));
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
            // IsChild yalnızca legacy migration işareti olarak kalır; çocuk
            // profili özelliği kaldırıldığı için düzenleme sırasında asla
            // değiştirilmez (yeni profiller çocuk profili olamaz).
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
            await DeleteProfileImportJobsAsync(db, profile.Id);

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
        await DeleteUnreferencedImportedPlaylistCopiesAsync(importedPlaylistCandidates);
        return profile;
    }

    private static async Task DeleteProfilePlaylistContentAsync(
        AppDbContext db,
        int profileId,
        CancellationToken cancellationToken = default)
    {
        var playlistIds = await db.Playlists
            .Where(pl => pl.ProfileId == profileId)
            .Select(pl => pl.Id)
            .ToListAsync(cancellationToken);

        if (playlistIds.Count == 0)
        {
            return;
        }

        // EpgProgram'ların Channel tablosuyla FK ilişkisi yoktur (ChannelId
        // tvg-id tabanlı bir string'dir) — kanallar cascade ile silinirken EPG
        // kayıtları geride kalır. Profil içeriği silinirken EPG açıkça temizlenir.
        var epgChannels = await db.Channels
            .Where(c => playlistIds.Contains(c.PlaylistId))
            .Select(c => new { c.TvgId, c.TvgName, c.Name })
            .ToListAsync(cancellationToken);

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
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        await db.Series
            .Where(s => playlistIds.Contains(s.PlaylistId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task DeleteProfileAsync(int profileId, int providerAccountId, ProfileAccessGrant grant)
    {
        if (_appPathService is null)
        {
            await DeleteProfileCoreAsync(profileId, providerAccountId, grant);
            return;
        }

        await ImportedPlaylistLifecycleGate.WaitAsync();
        try
        {
            await DeleteProfileCoreAsync(profileId, providerAccountId, grant);
        }
        finally
        {
            ImportedPlaylistLifecycleGate.Release();
        }
    }

    private async Task DeleteProfileCoreAsync(
        int profileId,
        int providerAccountId,
        ProfileAccessGrant grant)
    {
        _profileAccessService.ValidateOrThrow(grant, profileId, ProfileAccessPurpose.Delete);

        await using var db = await _contextFactory.CreateDbContextAsync();
        var importedPlaylistCandidates = await CollectProfileImportedPlaylistCandidatesAsync(
            db,
            [profileId]);

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

        // EPG (FK'sız) + ImportJob (FK'sız) kayıtları açıkça temizlenir;
        // aksi hâlde kanallar cascade ile silinse de bu satırlar yetim kalır.
        await DeleteProfilePlaylistContentAsync(db, profileId);
        await DeleteProfileImportJobsAsync(db, profileId);

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

        await DeleteUnreferencedImportedPlaylistCopiesAsync(importedPlaylistCandidates);
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
        if (_appPathService is null)
        {
            await PurgeExpiredProfilesCoreAsync();
            return;
        }

        await ImportedPlaylistLifecycleGate.WaitAsync();
        try
        {
            await PurgeExpiredProfilesCoreAsync();
        }
        finally
        {
            ImportedPlaylistLifecycleGate.Release();
        }
    }

    private async Task PurgeExpiredProfilesCoreAsync()
    {
        await using var db = await _contextFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow.AddDays(-3);

        var expired = await db.Profiles
            .Include(p => p.ProviderAccount)
            .Where(p => p.PendingDeletionAt.HasValue && p.PendingDeletionAt <= cutoff)
            .ToListAsync();

        if (expired.Count == 0)
        {
            return;
        }

        var expiredIds = expired.Select(p => p.Id).ToArray();
        var importedPlaylistCandidates = await CollectProfileImportedPlaylistCandidatesAsync(
            db,
            expiredIds);

        // İndirme dosyaları + indirme DB kayıtları ayrı servis/context üzerinden
        // temizlenir — dosya sistemi DB transaction'ına alınamaz (üstelik açık bir
        // SQLite yazma transaction'ı sırasında ikinci bir yazıcı "database is
        // locked" üretir). Önce tamamlanır; hata olursa DB'ye hiç dokunulmaz.
        // Not: ön-geçişin ortasında hata olursa daha önceki profillerin indirme
        // temizliği kısmen tamamlanmış kalır (DB'ye dokunulmaz; sonraki açılışta
        // idempotent tekrar ile tamamlanır).
        foreach (var profile in expired)
        {
            await _contentDownloadService.DeleteProfileDownloadsAsync(profile.Id);
        }

        // Kalan (silinmeyen) profillerin hesap kimlikleri önceden hesaplanır —
        // transaction içinde SaveChanges öncesi SQL okuma henüz silinmemiş
        // satırları görür. Böylece aynı hesabı paylaşan iki süresi dolmuş profil
        // de birlikte silinirken hesap yetim kalmaz (silinen kümenin dışındaki
        // bir profil tarafından kullanılıyorsa korunur).
        var remainingAccountIds = await db.Profiles
            .Where(p => !expiredIds.Contains(p.Id))
            .Select(p => p.ProviderAccountId)
            .Distinct()
            .ToListAsync();

        // DB tarafı TEK atomik transaction: EPG, series, import job, playlist,
        // profil ve hesap satırları birlikte işlenir. ExecuteDeleteAsync çağrıları
        // aynı transaction'a katılır — ortadaki bir adım başarısız olursa TAMAMI
        // geri alınır, yarım durum oluşmaz.
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var profile in expired)
            {
                await DeleteProfilePlaylistContentAsync(db, profile.Id);

                await DeleteProfileImportJobsAsync(db, profile.Id);

                await db.Playlists
                    .Where(p => p.ProfileId == profile.Id)
                    .ExecuteDeleteAsync();

                db.Profiles.Remove(profile);

                if (profile.ProviderAccount is { } account
                    && !remainingAccountIds.Contains(account.Id))
                {
                    db.ProviderAccounts.Remove(account);
                }
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await DeleteUnreferencedImportedPlaylistCopiesAsync(importedPlaylistCandidates);
        await CleanDeletedProfileSettingsAsync();
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

    public async Task<int> DeleteChildProfilesAsync(CancellationToken cancellationToken = default)
    {
        if (_appPathService is null)
        {
            return await DeleteChildProfilesCoreAsync(cancellationToken);
        }

        await ImportedPlaylistLifecycleGate.WaitAsync(cancellationToken);
        try
        {
            return await DeleteChildProfilesCoreAsync(cancellationToken);
        }
        finally
        {
            ImportedPlaylistLifecycleGate.Release();
        }
    }

    private async Task<int> DeleteChildProfilesCoreAsync(
        CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var childProfiles = await db.Profiles
            .Include(p => p.ProviderAccount)
            .Where(p => p.IsChild)
            .ToListAsync(cancellationToken);

        if (childProfiles.Count == 0)
        {
            return 0;
        }

        var importedPlaylistCandidates = await CollectProfileImportedPlaylistCandidatesAsync(
            db,
            childProfiles.Select(profile => profile.Id).ToArray(),
            cancellationToken);

        // 0) İndirme dosyaları + indirme DB kayıtları ayrı servis/context
        //    üzerinden temizlenir. Dosya sistemi DB transaction'ına alınamaz
        //    (üstelik açık bir SQLite yazma transaction'ı sırasında ikinci bir
        //    yazıcı "database is locked" üretir). Bu adım DB'ye dokunulmadan
        //    ÖNCE tamamlanır; hata olursa metod çıkar, profil kayıtları korunur
        //    ve migration bir sonraki açılışta tekrar denenir. Not: ön-geçişin
        //    ortasında hata olursa daha önceki profillerin indirme temizliği
        //    kısmen tamamlanmış kalabilir (DB'ye hiç dokunulmaz; sonraki açılışta
        //    idempotent tekrar ile tamamlanır). İndirmelerin commit SONRASINA
        //    alınması bu kısmi durumu da ortadan kaldırırdı ama o zaman indirme
        //    hatası profilin kendisini silme pahasına best-effort kalırdı;
        //    mevcut davranış bilinçli olarak "hata varsa profil de korunur"
        //    garantisini korur (bkz. DeleteChildProfilesAsync_DownloadFailure_
        //    PreservesProfileAndAccount).
        foreach (var profile in childProfiles)
        {
            await _contentDownloadService.DeleteProfileDownloadsAsync(profile.Id, cancellationToken);
        }

        // Yetim hesap denetimi, transaction içinde SaveChanges ÖNCESİ yapılamaz
        // — SQL okuma henüz silinmemiş satırları görür. Kalan (çocuk olmayan)
        // profillerin hesap kimlikleri önceden hesaplanır; silinen çocuk
        // profilleri yalnızca bu kümeye düşmeyen hesapları aday yapar (global
        // garbage collector değil, yalnız silinen çocuk profillerinin hesapları).
        var remainingAccountIds = await db.Profiles
            .Where(p => !p.IsChild)
            .Select(p => p.ProviderAccountId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // 1) DB tarafı TEK atomik transaction: EPG, series, import job, playlist,
        //    profil ve yetim hesap satırları birlikte işlenir. ExecuteDeleteAsync
        //    çağrıları aynı transaction'a katılır — ortadaki bir adım başarısız
        //    olursa TAMAMI geri alınır; yarım durum (ör. EPG silinmiş ama profil
        //    duruyor) oluşmaz. Kullanıcı verisi hiçbir zaman yarı silinmiş hâlde
        //    kalmaz ve migration bir sonraki açılışta tekrar denenebilir.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var profile in childProfiles)
            {
                await DeleteProfilePlaylistContentAsync(db, profile.Id, cancellationToken);

                await DeleteProfileImportJobsAsync(db, profile.Id, cancellationToken);

                await db.Playlists
                    .Where(p => p.ProfileId == profile.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                db.Profiles.Remove(profile);

                // Yalnızca başka hiçbir (kalan) profil tarafından kullanılmayan
                // hesaplar silinir. Aynı hesabı paylaşan kardeş çocuk profiller
                // aynı varlığı işaret ettiğinden ikinci Remove no-op'tur.
                if (profile.ProviderAccount is { } account
                    && !remainingAccountIds.Contains(account.Id))
                {
                    db.ProviderAccounts.Remove(account);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await DeleteUnreferencedImportedPlaylistCopiesAsync(importedPlaylistCandidates);

        // 2) Bildirim kalıcılığı — profil silme DB'ye commit edilir edilmez bayrak
        //    KAYDEDİLİR (best-effort). Kullanıcıya söz verilen bir defalık bilgi,
        //    sonraki cleanup adımlarının hatalarına rağmen kaybolmaz; buradaki bir
        //    hata sayının dönmesini engellememeli (çağıran ikinci şans olarak
        //    bayrağı tekrar yazabilir — idempotent).
        if (_settingsService is not null)
        {
            try
            {
                _settingsService.Settings.ChildModeRemovedNoticePending = true;
                await _settingsService.SaveAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ProfileService] Failed to persist child-mode removal notice: {ex.Message}");
            }
        }

        // 3) Best-effort: ayar temizliği hatası, silinen sayının dönmesini
        //    engellememeli — kalan yetim ayar kayıtları sonraki açılışlarda
        //    GetProfilesAsync üzerinden tekrar temizlenir.
        try
        {
            await CleanDeletedProfileSettingsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ProfileService] Failed to clean settings after child profile deletion: {ex.Message}");
        }

        return childProfiles.Count;
    }

    private static async Task<List<string>> CollectProfileImportedPlaylistCandidatesAsync(
        AppDbContext db,
        IReadOnlyCollection<int> profileIds,
        CancellationToken cancellationToken = default)
    {
        if (profileIds.Count == 0)
        {
            return [];
        }

        var ids = profileIds.ToArray();
        var playlistPaths = await db.Playlists
            .Where(playlist =>
                playlist.ProfileId.HasValue
                && ids.Contains(playlist.ProfileId.Value)
                && playlist.FilePath != null)
            .OrderBy(playlist => playlist.Id)
            .Select(playlist => playlist.FilePath!)
            .ToListAsync(cancellationToken);

        var accountUrls = await db.Profiles
            .Where(profile => ids.Contains(profile.Id) && profile.ProviderAccount != null)
            .Select(profile => profile.ProviderAccount!.Url)
            .ToListAsync(cancellationToken);

        playlistPaths.AddRange(accountUrls);
        return playlistPaths;
    }

    private async Task DeleteUnreferencedImportedPlaylistCopiesAsync(
        IEnumerable<string?> candidates)
    {
        if (_appPathService is null)
        {
            return;
        }

        try
        {
            var importsDirectory = Path.GetFullPath(
                Path.Combine(_appPathService.UserDataDirectory, "Imports"));
            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var seenCandidates = new HashSet<string>(pathComparer);
            var normalizedCandidates = new List<string>();

            foreach (var candidate in candidates)
            {
                if (TryNormalizeManagedImportPath(candidate, importsDirectory, out var normalized)
                    && seenCandidates.Add(normalized))
                {
                    normalizedCandidates.Add(normalized);
                }
            }

            if (normalizedCandidates.Count == 0)
            {
                return;
            }

            await using var db = await _contextFactory.CreateDbContextAsync();
            var remainingPlaylistPaths = await db.Playlists
                .Where(playlist => playlist.FilePath != null)
                .Select(playlist => playlist.FilePath!)
                .ToListAsync();
            var remainingAccountUrls = await db.ProviderAccounts
                .Select(account => account.Url)
                .ToListAsync();

            var remainingReferences = new HashSet<string>(pathComparer);
            foreach (var reference in remainingPlaylistPaths.Concat(remainingAccountUrls))
            {
                if (TryNormalizeManagedImportPath(reference, importsDirectory, out var normalized))
                {
                    remainingReferences.Add(normalized);
                }
            }

            foreach (var candidate in normalizedCandidates)
            {
                if (remainingReferences.Contains(candidate))
                {
                    continue;
                }

                try
                {
                    File.Delete(candidate);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "Failed to delete unreferenced imported playlist copy {Path}.",
                        candidate);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to clean imported playlist copies.");
            System.Diagnostics.Debug.WriteLine(
                $"[ProfileService] Failed to clean imported playlist copies: {ex.Message}");
        }
    }

    private void ValidateManagedImportSourceExists(ProfileSaveRequest request)
    {
        if (_appPathService is null || request.AccountType != ProfileType.M3U)
        {
            return;
        }

        try
        {
            var importsDirectory = Path.GetFullPath(
                Path.Combine(_appPathService.UserDataDirectory, "Imports"));
            if (TryNormalizeManagedImportPath(request.Url, importsDirectory, out var managedPath)
                && !File.Exists(managedPath))
            {
                throw new FileNotFoundException(
                    "The selected imported playlist copy no longer exists.",
                    managedPath);
            }
        }
        catch (FileNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to validate the imported playlist source path.");
        }
    }

    private static bool TryNormalizeManagedImportPath(
        string? value,
        string importsDirectory,
        out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var candidate = value.Trim().Trim('"');
            if (candidate.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out var fileUri) || !fileUri.IsFile)
                {
                    return false;
                }

                candidate = fileUri.LocalPath;
            }

            if (!Path.IsPathRooted(candidate))
            {
                return false;
            }

            var fullPath = Path.GetFullPath(candidate);
            var parentDirectory = Path.GetDirectoryName(fullPath);
            if (parentDirectory is null)
            {
                return false;
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!string.Equals(
                    Path.TrimEndingDirectorySeparator(parentDirectory),
                    Path.TrimEndingDirectorySeparator(importsDirectory),
                    comparison))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ImportJob kayıtlarının profil/playlist ile FK ilişkisi yoktur; profil
    /// silinirken açıkça temizlenmeleri gerekir (kayıtlar kalıcı yetime dönüşür).
    /// </summary>
    private static async Task DeleteProfileImportJobsAsync(
        AppDbContext db,
        int profileId,
        CancellationToken cancellationToken = default)
    {
        var playlistIds = await db.Playlists
            .Where(p => p.ProfileId == profileId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        await db.ImportJobs
            .Where(j => j.ProfileId == profileId
                || (j.PlaylistId != null && playlistIds.Contains(j.PlaylistId.Value)))
            .ExecuteDeleteAsync(cancellationToken);
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
        string pin)
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

            // Doğrulama yetkisi servistedir: güncel verifier çağırandan değil,
            // veritabanındaki profile.PinHash'ten okunur. Böylece PIN ekranı
            // açıkken PIN değiştirilse bile eski verifier doğrulamayı etkilemez;
            // iç çağıranlar kendi verifier'ını üreterek sonucu değiştiremez.
            var storedVerifier = profile.PinHash;

            // Savunma: PIN'siz (boş PinHash) profil için sayaç kirletilmez —
            // UI bu profillerde kapıyı hiç açmaz; servis yalnızca bağımsız
            // olarak da güvenli davranır (sayacı artırmadan reddeder).
            if (string.IsNullOrWhiteSpace(storedVerifier))
            {
                return new ProfilePinAttemptResult(
                    false,
                    ToPinVerificationState(profile, now));
            }

            if (_pinService.Verify(pin, storedVerifier))
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
