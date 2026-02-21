using System.Security.Cryptography;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class SecurityService : ISecurityService
{
    // DPAPI entropy to make the encryption even more specific to this app
    private static readonly byte[] Entropy = "N0ctra_P1ayer_Security_Entropy_2024"u8.ToArray();

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        try
        {
            var data = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch
        {
            // Fallback: If encryption fails for some reason (rare on Windows), 
            // we return the plain text or handle accordingly. 
            // In a real app, you might want to log this.
            return plainText;
        }
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return null;

        try
        {
            var data = Convert.FromBase64String(cipherText);
            var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // If decryption fails, it might be plain text from an older version or corrupted.
            return cipherText;
        }
    }
}
