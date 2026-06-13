using System.Security.Cryptography;
using System.Text;
using Noctra.Services.Interfaces;

namespace Noctra.Services;

public class DesktopSecurityService : ISecurityService
{
    // DPAPI entropy to make the encryption even more specific to this app
    private static readonly byte[] Entropy = "N0ctra_P1ayer_Security_Entropy_2024"u8.ToArray();
    private const string PinHashAlgorithm = "PBKDF2";
    private const string PinHashPrf = "SHA256";
    private const int PinHashIterations = 210_000;
    private const int PinSaltSizeBytes = 16;
    private const int PinHashSizeBytes = 32;

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
            // If decryption fails, it might be plain text from an older version or corrupted.
            return cipherText;
        }
    }

    public string HashPin(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(PinSaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            PinHashIterations,
            HashAlgorithmName.SHA256,
            PinHashSizeBytes);

        return string.Join('$',
            PinHashAlgorithm,
            PinHashPrf,
            PinHashIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPin(string pin, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        if (TryVerifyPbkdf2Pin(pin, hash))
        {
            return true;
        }

        return VerifyLegacySha256Pin(pin, hash);
    }

    private static bool TryVerifyPbkdf2Pin(string pin, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 5 ||
            !string.Equals(parts[0], PinHashAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(parts[1], PinHashPrf, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations) ||
            iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expectedHash = Convert.FromBase64String(parts[4]);
            if (salt.Length < PinSaltSizeBytes || expectedHash.Length != PinHashSizeBytes)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool VerifyLegacySha256Pin(string pin, string hash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"NOCTRA_PIN_{pin}"));
        var legacyHash = Convert.ToHexString(bytes);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(legacyHash),
            Encoding.ASCII.GetBytes(hash.ToUpperInvariant()));
    }

}

public sealed class SecurityService : DesktopSecurityService
{
}
