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
    /// </summary>
    Task DeleteProfileAsync(int profileId, int providerAccountId);

    /// <summary>
    /// Checks if a duplicate provider account already exists (excluding the given account ID).
    /// </summary>
    Task<bool> CheckDuplicateAccountAsync(
        int excludeAccountId, ProfileType type, string url, string username, string password);

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
}

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
}

public record ExistingProfileIds(int ProfileId, int ProviderAccountId);
