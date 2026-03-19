namespace Noctra.Services.Interfaces;

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
    /// 4 haneli PIN'in SHA256 hash'ini üretir.
    /// </summary>
    string HashPin(string pin);

    /// <summary>
    /// PIN'in verilen hash ile eşleşip eşleşmediğini doğrular.
    /// </summary>
    bool VerifyPin(string pin, string hash);
}
