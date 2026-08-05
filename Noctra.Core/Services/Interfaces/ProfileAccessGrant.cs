using System;

namespace Noctra.Services.Interfaces;

/// <summary>
/// Korumalı profil işlemleri için hangi yetki seviyesinin gerekli olduğu.
/// </summary>
public enum ProfileAccessPurpose
{
    /// <summary>Profil girişi / yükleme.</summary>
    Load,

    /// <summary>Profil düzenleme (düzenleme oturumu silme ve PIN değişikliğini de kapsar).</summary>
    Edit,

    /// <summary>Profil silme.</summary>
    Delete,

    /// <summary>PIN değiştirme.</summary>
    PinChange
}

/// <summary>
/// PIN doğrulamasından sonra üretilen kısa ömürlü yetki nesnesi.
/// Tüm korumalı profil işlemleri (yükleme, düzenleme, PIN değiştirme, silme)
/// bu grant'i talep eder; View code-behind dışındaki kod yolları da kapıyı atlayamaz.
/// </summary>
public sealed record ProfileAccessGrant(int ProfileId, ProfileAccessPurpose Purpose, DateTime ExpiresAtUtc)
{
    /// <summary>Grant geçerlilik süresi (dakika).</summary>
    public const double ValidityMinutes = 5;

    public static ProfileAccessGrant Create(int profileId, ProfileAccessPurpose purpose)
        => new(profileId, purpose, DateTime.UtcNow.AddMinutes(ValidityMinutes));

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;

    /// <summary>
    /// Grant'in verilen profil ve amaç için geçerli olup olmadığı.
    /// Edit grant'i silme ve PIN değişikliğini de kapsar (kullanıcı PIN'ini
    /// düzenleme ekranına girerken zaten doğruladı), ama profil yükleme
    /// her zaman kendi doğrulamasını ister.
    /// </summary>
    public bool Authorizes(int profileId, ProfileAccessPurpose purpose)
    {
        if (ProfileId != profileId || IsExpired)
        {
            return false;
        }

        if (Purpose == purpose)
        {
            return true;
        }

        return Purpose == ProfileAccessPurpose.Edit
            && purpose is ProfileAccessPurpose.Delete or ProfileAccessPurpose.PinChange;
    }
}

/// <summary>
/// Korumalı bir profil işlemi geçerli bir ProfileAccessGrant olmadan
/// çağrıldığında fırlatılır.
/// </summary>
public sealed class ProfileAccessDeniedException : Exception
{
    public ProfileAccessDeniedException()
        : base("Bu işlem için geçerli bir profil erişim yetkisi gerekli.")
    {
    }

    public ProfileAccessDeniedException(string message)
        : base(message)
    {
    }
}
