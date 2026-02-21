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
}
