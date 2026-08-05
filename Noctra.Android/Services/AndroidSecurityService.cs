using System;
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
