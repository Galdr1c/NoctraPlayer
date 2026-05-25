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
    /// 4 haneli PIN icin salt'li ve surumlu hash uretir.
    /// </summary>
    string HashPin(string pin);

    /// <summary>
    /// PIN'in verilen hash ile eslesip eslesmedigini dogrular.
    /// </summary>
    bool VerifyPin(string pin, string hash);
}
