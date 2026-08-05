using Noctra.Models;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Profil erişim yetkilerinin (grant) merkezî üretimi ve doğrulaması.
/// PIN doğrulaması başarılı olduğunda kısa ömürlü ProfileAccessGrant üretilir;
/// korumalı işlemler (yükleme, düzenleme, PIN değiştirme, silme) bu grant'i talep eder.
/// </summary>
public interface IProfileAccessService
{
    /// <summary>
    /// Profil için bir erişim yetkisi elde etmeye çalışır.
    /// PIN'siz profiller için doğrulama gerekmeden grant üretilir.
    /// PIN'li profiller için verifyPin callback'i çağrılır (UI PIN ekranı).
    /// Başarısız doğrulamada null döner.
    /// </summary>
    Task<ProfileAccessGrant?> TryAcquireAsync(
        Profile profile,
        ProfileAccessPurpose purpose,
        Func<Profile, ProfileAccessPurpose, Task<bool>> verifyPin);

    /// <summary>
    /// Grant'in verilen profil ve amaç için geçerli olduğunu doğrular;
    /// geçersizse ProfileAccessDeniedException fırlatır.
    /// </summary>
    void ValidateOrThrow(ProfileAccessGrant? grant, int profileId, ProfileAccessPurpose purpose);
}
