using System.Security.Cryptography;
using System.Text;

namespace Noctra.Services;

/// <summary>
/// 4 haneli ASCII PIN için hızlı, salt'lı SHA-256 doğrulayıcı.
///
/// Tehdit modeli: PIN, aynı cihazdaki başka bir kişinin profile kolayca
/// girmesini zorlaştıran yerel bir kilittir. Veritabanını, indirilen dosyaları
/// veya hesap parolasını şifrelemez. Bu sözleşme için 210.000 iterasyonlu
/// PBKDF2 gereksiz ağırdı; hızlı SHA-256 + 16 byte rastgele salt + sabit
/// zamanlı karşılaştırma + 5 hata / 30 saniye merkezî kilit yeterlidir.
///
/// Saklama formatı: PIN2$&lt;salt-base64&gt;$&lt;hash-base64&gt;
/// Hash: SHA256("NOCTRA_PROFILE_PIN_V2\0" + salt + pin)
/// </summary>
public static class ProfilePinVerifier
{
    private const string FormatPrefix = "PIN2";
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    // Domain ayrımı: PIN hash'inin başka bir veri türüyle (ör. parola) aynı
    // değeri üretmesini engeller. NUL sonlandırıcı, uzunluk belirsizliğini ortadan
    // kaldırır (ör. "12" + "34" vs "123" + "4" çakışmaları).
    private static ReadOnlySpan<byte> Domain => "NOCTRA_PROFILE_PIN_V2\0"u8;

    /// <summary>
    /// 4 haneli ASCII PIN için salt'lı, sürümlü verifier üretir.
    /// </summary>
    /// <exception cref="ArgumentException">PIN tam olarak 4 ASCII rakam değilse.</exception>
    public static string Create(string pin)
    {
        if (!IsValidPin(pin))
        {
            throw new ArgumentException("PIN must be exactly four ASCII digits.", nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var pinBytes = Encoding.ASCII.GetBytes(pin);

        var payload = new byte[Domain.Length + salt.Length + pinBytes.Length];
        Domain.CopyTo(payload);
        salt.CopyTo(payload.AsSpan(Domain.Length));
        pinBytes.CopyTo(payload.AsSpan(Domain.Length + salt.Length));

        var hash = SHA256.HashData(payload);

        CryptographicOperations.ZeroMemory(payload);
        CryptographicOperations.ZeroMemory(pinBytes);

        return string.Join(
            '$',
            FormatPrefix,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    /// <summary>
    /// PIN'in saklanan verifier ile eşleşip eşleşmediğini sabit zamanlı doğrular.
    /// Bozuk/elle kurcalanmış kayıtlarda yanlış formatlı girdilere karşı güvenli
    /// biçimde false döner.
    /// </summary>
    public static bool Verify(string pin, string storedVerifier)
    {
        if (!IsValidPin(pin) || string.IsNullOrWhiteSpace(storedVerifier))
        {
            return false;
        }

        if (!TryParseVerifierParts(storedVerifier, out var salt, out var expectedHash))
        {
            return false;
        }

        var pinBytes = Encoding.ASCII.GetBytes(pin);

        var payload = new byte[Domain.Length + salt.Length + pinBytes.Length];
        Domain.CopyTo(payload);
        salt.CopyTo(payload.AsSpan(Domain.Length));
        pinBytes.CopyTo(payload.AsSpan(Domain.Length + salt.Length));

        var actualHash = SHA256.HashData(payload);
        var result = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);

        CryptographicOperations.ZeroMemory(payload);
        CryptographicOperations.ZeroMemory(pinBytes);
        CryptographicOperations.ZeroMemory(actualHash);

        return result;
    }

    /// <summary>
    /// Kayıt güncel PIN2 formatındaysa true döner. Eski PBKDF2/legacy SHA-256
    /// kayıtları için false — bu formatlar desteklenmez ve geçiş sırasında
    /// sıfırlanır (bkz. DatabaseSchemaFixupService).
    ///
    /// Yalnızca önek kontrolü yapar; gerçek format doğrulaması için
    /// <see cref="IsWellFormed"/> kullanılmalıdır.
    /// </summary>
    public static bool IsCurrentFormat(string? value)
        => value?.StartsWith(FormatPrefix + "$", StringComparison.Ordinal) == true;

    /// <summary>
    /// Kaydın tam PIN2 formatına uyduğunu doğrular: PIN2$ öneki, tam 3 parça,
    /// salt ve hash'in geçerli Base64 olması ve doğru uzunlukları (16/32 bayt).
    ///
    /// IsCurrentFormat'ın aksine, PIN2$ önekli fakat bozuk/elle kurcalanmış
    /// kayıtları da yakalar — böyle kayıtlar <see cref="Verify"/>'de asla
    /// doğrulanamaz ve DatabaseSchemaFixupService geçiş sırasında sıfırlar.
    /// </summary>
    public static bool IsWellFormed(string? value)
        => value is not null && TryParseVerifierParts(value, out _, out _);

    /// <summary>
    /// Saklanan verifier'ın PIN2 format kurallarına tam uyduğunu doğrular ve
    /// salt/hash'i çözer: PIN2$ öneki, tam 3 parça, geçerli Base64 ve doğru
    /// uzunluklar (16/32 bayt). Format kurallarının TEK kaynağıdır — hem
    /// <see cref="Verify"/> hem <see cref="IsWellFormed"/> burayı kullanır;
    /// sabitler değişirse tek yer güncellenir.
    /// </summary>
    private static bool TryParseVerifierParts(string storedVerifier, out byte[] salt, out byte[] hash)
    {
        salt = [];
        hash = [];

        if (string.IsNullOrWhiteSpace(storedVerifier) ||
            !storedVerifier.StartsWith(FormatPrefix + "$", StringComparison.Ordinal))
        {
            return false;
        }

        var parts = storedVerifier.Split('$');
        if (parts.Length != 3 || !string.Equals(parts[0], FormatPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var parsedSalt = Convert.FromBase64String(parts[1]);
            var parsedHash = Convert.FromBase64String(parts[2]);

            if (parsedSalt.Length != SaltSizeBytes || parsedHash.Length != HashSizeBytes)
            {
                return false;
            }

            salt = parsedSalt;
            hash = parsedHash;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidPin(string? pin)
        => pin is
            [>= '0' and <= '9',
             >= '0' and <= '9',
             >= '0' and <= '9',
             >= '0' and <= '9'];
}
