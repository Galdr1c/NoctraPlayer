using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class SecurityService : ISecurityService
{
    // DPAPI entropy to make the encryption even more specific to this app
    private static readonly byte[] Entropy = "N0ctra_P1ayer_Security_Entropy_2024"u8.ToArray();
    // Key for AES encryption on non-Windows platforms
    private static readonly Lazy<byte[]> AesKey = new(LoadOrCreateKey);

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return EncryptAes(plainText);
        }

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

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DecryptAes(cipherText);
        }

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

    private static string? EncryptAes(string plainText)
    {
        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var iv = new byte[12]; // 12 bytes IV for GCM
            RandomNumberGenerator.Fill(iv);

            var cipherText = new byte[plainBytes.Length];
            var tag = new byte[16]; // 16 bytes auth tag

            using (var aes = new AesGcm(AesKey.Value, 16))
            {
                aes.Encrypt(iv, plainBytes, cipherText, tag);
            }

            var result = new byte[iv.Length + tag.Length + cipherText.Length];
            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(tag, 0, result, iv.Length, tag.Length);
            Buffer.BlockCopy(cipherText, 0, result, iv.Length + tag.Length, cipherText.Length);

            return Convert.ToBase64String(result);
        }
        catch
        {
            return null;
        }
    }

    private static string DecryptAes(string cipherText)
    {
        try
        {
            var combined = Convert.FromBase64String(cipherText);
            if (combined.Length < 12 + 16) return cipherText;

            var iv = new byte[12];
            var tag = new byte[16];
            var cipherBytes = new byte[combined.Length - 12 - 16];

            Buffer.BlockCopy(combined, 0, iv, 0, 12);
            Buffer.BlockCopy(combined, 12, tag, 0, 16);
            Buffer.BlockCopy(combined, 28, cipherBytes, 0, cipherBytes.Length);

            var plainBytes = new byte[cipherBytes.Length];

            using (var aes = new AesGcm(AesKey.Value, 16))
            {
                aes.Decrypt(iv, cipherBytes, tag, plainBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return cipherText;
        }
    }

    private static byte[] LoadOrCreateKey()
    {
        try
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Noctra");
            if (!Directory.Exists(appData))
            {
                Directory.CreateDirectory(appData);
            }

            var keyPath = Path.Combine(appData, "secret.key");
            if (File.Exists(keyPath))
            {
                var key = File.ReadAllBytes(keyPath);
                if (key.Length == 32)
                {
                    return key;
                }
            }

            var newKey = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(keyPath, newKey);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // Ignore if not supported
                }
            }
            return newKey;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initialize encryption key.", ex);
        }
    }
}
