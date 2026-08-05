namespace Noctra.Services.Interfaces;

/// <summary>
/// PIN dogrulama sonucu. <see cref="ValidNeedsRehash"/> degeri, PIN'in dogru
/// oldugunu ancak hash'in eski (legacy SHA-256 veya eski parametreli) formatta
/// saklandigini ve ayni PIN ile aninda yeni formatta yeniden hash'lenmesi
/// gerektigini belirtir.
/// </summary>
public enum PinVerificationResult
{
    /// <summary>PIN yanlis veya hash bozuk/format disi.</summary>
    Invalid,

    /// <summary>PIN dogru ve hash guncel formatta.</summary>
    Valid,

    /// <summary>PIN dogru ancak hash eski formatta — yeniden hash'lenmeli.</summary>
    ValidNeedsRehash,
}

/// <summary>
/// Hassas verileri (şifreler vb.) güvenli bir şekilde saklamak için kullanılan servis.
/// Windows DPAPI (Data Protection API) kullanır.
/// </summary>
public interface ISecurityService
{
    /// <summary>
    /// Metni mevcut kullanıcı ve makine için şifreler.
    /// </summary>
    string? Encrypt(string? plainText);

    /// <summary>
    /// Şifrelenmiş metni çözer.
    /// </summary>
    string? Decrypt(string? cipherText);

    /// <summary>
    /// 4 haneli PIN icin salt'li ve surumlu hash uretir.
    /// </summary>
    string HashPin(string pin);

    /// <summary>
    /// PIN'in verilen hash ile eslesip eslesmedigini dogrular.
    /// </summary>
    PinVerificationResult VerifyPin(string pin, string hash);

    /// <summary>
    /// <see cref="VerifyPin"/> ile ayni islemi UI thread'ini bloklamadan
    /// arka planda calistirir.
    /// </summary>
    Task<PinVerificationResult> VerifyPinAsync(string pin, string hash);
}
