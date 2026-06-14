using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Noctra.Services.Interfaces;

namespace Noctra.Android.Services;

public sealed class AndroidSecurityService : ISecurityService
{
    private const string KeyStoreProvider = "AndroidKeyStore";
    private const string KeyAlias = "studio.kynora.noctra.credentials.v1";
    private const string CipherTransformation = "AES/GCM/NoPadding";
    private const byte PayloadVersion = 1;
    private const string PinHashAlgorithm = "PBKDF2";
    private const string PinHashPrf = "SHA256";
    private const int PinHashIterations = 210_000;
    private const int PinSaltSizeBytes = 16;
    private const int PinHashSizeBytes = 32;

    public string? Encrypt(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return null;
        }

        var cipher = Cipher.GetInstance(CipherTransformation)
            ?? throw new CryptographicException("Android AES-GCM cipher is unavailable.");
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, GetOrCreateKey());

        var initializationVector = cipher.GetIV()
            ?? throw new CryptographicException("Android AES-GCM did not produce an IV.");
        var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(plainText))
            ?? throw new CryptographicException("Android AES-GCM encryption failed.");
        if (initializationVector.Length > byte.MaxValue)
        {
            throw new CryptographicException("Android AES-GCM IV is unexpectedly large.");
        }

        var payload = new byte[2 + initializationVector.Length + encrypted.Length];
        payload[0] = PayloadVersion;
        payload[1] = (byte)initializationVector.Length;
        Buffer.BlockCopy(initializationVector, 0, payload, 2, initializationVector.Length);
        Buffer.BlockCopy(encrypted, 0, payload, 2 + initializationVector.Length, encrypted.Length);
        return Convert.ToBase64String(payload);
    }

    public string? Decrypt(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return null;
        }

        try
        {
            var payload = Convert.FromBase64String(cipherText);
            if (payload.Length < 3 || payload[0] != PayloadVersion)
            {
                return null;
            }

            var initializationVectorLength = payload[1];
            if (initializationVectorLength == 0 ||
                payload.Length <= 2 + initializationVectorLength)
            {
                return null;
            }

            var initializationVector = payload.AsSpan(2, initializationVectorLength).ToArray();
            var encrypted = payload.AsSpan(2 + initializationVectorLength).ToArray();
            var cipher = Cipher.GetInstance(CipherTransformation);
            if (cipher is null)
            {
                return null;
            }

            cipher.Init(
                Javax.Crypto.CipherMode.DecryptMode,
                GetOrCreateKey(),
                new GCMParameterSpec(128, initializationVector));
            var decrypted = cipher.DoFinal(encrypted);
            return decrypted is null ? null : Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (
            ex is FormatException or
            GeneralSecurityException or
            CryptographicException)
        {
            return null;
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

        return string.Join(
            '$',
            PinHashAlgorithm,
            PinHashPrf,
            PinHashIterations.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPin(string pin, string hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            return false;
        }

        var parts = hash.Split('$');
        if (parts.Length == 5 &&
            string.Equals(parts[0], PinHashAlgorithm, StringComparison.Ordinal) &&
            string.Equals(parts[1], PinHashPrf, StringComparison.Ordinal) &&
            int.TryParse(
                parts[2],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var iterations) &&
            iterations > 0)
        {
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

        var legacyHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"NOCTRA_PIN_{pin}")));
        return hash.Length == legacyHash.Length &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(legacyHash),
                Encoding.ASCII.GetBytes(hash.ToUpperInvariant()));
    }

    private static IKey GetOrCreateKey()
    {
        var keyStore = KeyStore.GetInstance(KeyStoreProvider)
            ?? throw new CryptographicException("Android Keystore is unavailable.");
        keyStore.Load(null);

        if (keyStore.ContainsAlias(KeyAlias))
        {
            return keyStore.GetKey(KeyAlias, null)
                ?? throw new CryptographicException("Android Keystore key could not be loaded.");
        }

        var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, KeyStoreProvider)
            ?? throw new CryptographicException("Android Keystore AES generator is unavailable.");
        using var specification = new KeyGenParameterSpec.Builder(
                KeyAlias,
                KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .Build();
        keyGenerator.Init(specification);
        return keyGenerator.GenerateKey()
            ?? throw new CryptographicException("Android Keystore key generation failed.");
    }
}
