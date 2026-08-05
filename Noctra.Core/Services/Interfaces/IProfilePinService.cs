namespace Noctra.Services.Interfaces;

/// <summary>
/// Profil PIN doğrulayıcı. PIN2 (salt'lı SHA-256) formatında verifier üretir ve
/// doğrular. Eski PBKDF2 / legacy SHA-256 akışı yoktur; doğrulama senkrondur ve
/// mikrosaniye mertebesinde tamamlanır — spinner veya background thread gerekmez.
///
/// Asıl pratik koruma katmanı ProfileService'in merkezî deneme sınırıdır
/// (5 hata / 30 saniye kilit, veritabanında kalıcı).
/// </summary>
public interface IProfilePinService
{
    /// <summary>
    /// 4 haneli ASCII PIN için salt'lı, sürümlü verifier üretir.
    /// </summary>
    string CreateVerifier(string pin);

    /// <summary>
    /// PIN'in saklanan verifier ile eşleşip eşleşmediğini sabit zamanlı doğrular.
    /// </summary>
    bool Verify(string pin, string storedVerifier);
}
