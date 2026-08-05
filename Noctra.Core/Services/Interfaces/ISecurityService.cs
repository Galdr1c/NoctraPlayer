namespace Noctra.Services.Interfaces;

/// <summary>
/// Hassas verileri (şifreler, sağlayıcı parolaları vb.) güvenli bir şekilde
/// saklamak için kullanılan servis. Masaüstünde Windows DPAPI, Android'de
/// Android Keystore (AES-GCM) kullanır.
///
/// Not: Profil PIN doğrulaması bu serviste DEĞİL, <see cref="IProfilePinService"/>
/// üzerinde yapılır (hızlı salt'lı SHA-256, PIN2 formatı).
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
