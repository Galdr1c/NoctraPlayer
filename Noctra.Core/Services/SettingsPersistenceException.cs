using System;

namespace Noctra.Services;

/// <summary>
/// Ayarların diske yazılamaması durumunda fırlatılır.
/// SaveAsync'in başarısızlığı yutması yerine, çağıranların kayıt
/// durumunu doğrulayabilmesi için yazma hatalarını yukarı taşır.
/// </summary>
public sealed class SettingsPersistenceException : Exception
{
    public SettingsPersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
