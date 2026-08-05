using System.Security.Cryptography;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class DesktopSecurityService : ISecurityService
{
    // DPAPI entropy to make the encryption even more specific to this app
    private static readonly byte[] Entropy = "N0ctra_P1ayer_Security_Entropy_2024"u8.ToArray();

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Desktop DPAPI credential protection is available only on Windows.");
        }

        var data = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return null;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Desktop DPAPI credential protection is available only on Windows.");
        }

        try
        {
            var data = Convert.FromBase64String(cipherText);
            var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return null;
        }
    }

}

public sealed class SecurityService : DesktopSecurityService
{
}
