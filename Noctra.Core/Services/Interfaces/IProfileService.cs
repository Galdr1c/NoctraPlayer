using Noctra.Models;

namespace Noctra.Services.Interfaces;

public interface IProfileService
{
    /// <summary>
    /// Saves a profile and its provider account. Creates new or updates existing.
    /// Returns the saved profile on success, null if limit exceeded.
    /// </summary>
    Task<Profile?> SaveProfileAsync(ProfileSaveRequest request);

    /// <summary>
    /// Deletes a profile and all related data (watch history, playlists, downloads).
    /// Cleans up orphaned ProviderAccount if no other profiles reference it.
    /// Requires a valid ProfileAccessGrant (Delete) — PIN gate is enforced here,
    /// not only in the View layer.
    /// </summary>
    Task DeleteProfileAsync(int profileId, int providerAccountId, ProfileAccessGrant grant);

    /// <summary>
    /// Gets all profiles ordered by last used (descending), including their provider accounts.
    /// </summary>
    Task<List<Profile>> GetProfilesAsync();

    /// <summary>
    /// Updates the LastUsed timestamp of a profile.
    /// </summary>
    Task UpdateLastUsedAsync(int profileId);

    /// <summary>
    /// 3 günlük silme geri sayımını başlatır (PIN unutuldu akışı).
    /// </summary>
    Task ScheduleProfileDeletionAsync(int profileId);

    /// <summary>
    /// Geri sayımı iptal eder (PIN hatırlanıp başarılı giriş yapıldığında).
    /// </summary>
    Task CancelProfileDeletionAsync(int profileId);

    /// <summary>
    /// Süresi dolmuş profilleri kalıcı olarak siler (uygulama açılışında çağrılır).
    /// </summary>
    Task PurgeExpiredProfilesAsync();

    /// <summary>
    /// PIN doğrulama durumunu döndürür (kalıcı deneme sayacı ve kilit).
    /// Süresi dolmuş kilit temizlenir ve sayaç sıfırlanır.
    /// </summary>
    Task<PinVerificationState> GetPinVerificationStateAsync(int profileId);

    /// <summary>
    /// PIN doğrulama ile deneme sayacı/kilit güncellemesini TEK atomik işlemde
    /// yapar. Servis profil bazında serileştirir (per-profile semaphore); art arda
    /// veya eşzamanlı denemeler veritabanındaki sayacı kaybettirmez (race yok).
    ///
    /// - Doğru PIN: sayaç sıfırlanır, kilit temizlenir.
    /// - Yanlış PIN: sayaç artırılır; eşiğe ulaşılırsa kalıcı kilit yazılır.
    /// - Profil zaten kilitliyse doğrulamaya girilmez, güncel kilit durumu döner.
    ///
    /// Çağıran (PinEntryViewModel) keypad'i bu çağrı süresince devre dışı
    /// bırakmalıdır — böylece her deneme kalıcı state'e işlenmeden bir sonraki
    /// deneme başlayamaz.
    /// </summary>
    Task<ProfilePinAttemptResult> VerifyAttemptAsync(
        int profileId,
        string pin,
        string verifier);

    /// <summary>
    /// Art arda başarısız PIN denemesini kaydeder; eşiğe ulaşıldığında
    /// profili kalıcı olarak kilitler. Yeni durumu döndürür.
    /// </summary>
    Task<PinVerificationState> RegisterPinFailureAsync(int profileId);

    /// <summary>
    /// Başarılı PIN doğrulamasında deneme sayacını ve kilidi sıfırlar.
    /// </summary>
    Task ResetPinAttemptsAsync(int profileId);
}

/// <summary>
/// Bir profilin PIN doğrulama durumu (kalıcı, veritabanında saklanır).
/// </summary>
public sealed record PinVerificationState(
    int FailedPinAttempts,
    DateTime? PinLockedUntilUtc,
    bool IsLocked,
    TimeSpan? RemainingLockDuration);

/// <summary>
/// Atomik PIN denemesi sonucu. Doğrulama sonucu ve güncel kalıcı durum
/// (deneme sayacı / kilit) tek servis çağrısında birlikte döner — UI ayrıca
/// persist etmez, böylece gecikmiş yazmalar birbirini ezemez.
/// </summary>
public sealed record ProfilePinAttemptResult(
    bool IsValid,
    PinVerificationState State);

/// <summary>
/// Immutable request object for profile save operations.
/// Separates UI state from DB persistence.
/// </summary>
public record ProfileSaveRequest
{
    public required string ProfileName { get; init; }
    public required string Avatar { get; init; }
    public required bool IsChild { get; init; }
    public required string Url { get; init; }
    public required string Username { get; init; }
    public required string EncryptedPassword { get; init; }
    public required ProfileType AccountType { get; init; }
    public bool CredentialsChanged { get; init; }

    /// <summary>
    /// PIN hash. Null = PIN kaldir, deger = PIN ayarla/guncelle.
    /// </summary>
    public string? PinHash { get; init; }

    /// <summary>
    /// When editing an existing profile, provides the IDs needed for update.
    /// Null when creating a new profile.
    /// </summary>
    public ExistingProfileIds? ExistingIds { get; init; }

    /// <summary>
    /// Merkezî erişim yetkisi. PIN korumalı bir profilin düzenlenmesi
    /// (ad/avatar/PIN değişikliği) geçerli bir grant ister — Edit yeterli;
    /// PIN değişikliği PinChange (Edit grant'i PinChange'i de kapsar).
    /// Yeni profil oluşturma grant gerektirmez.
    /// </summary>
    public ProfileAccessGrant? AccessGrant { get; init; }
}

public record ExistingProfileIds(int ProfileId, int ProviderAccountId);
