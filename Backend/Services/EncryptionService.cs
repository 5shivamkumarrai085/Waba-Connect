using System.Security.Cryptography;
using System.Text;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Services;

/// <summary>
/// Field-level encryption for stored credentials (SMTP/IMAP passwords, AWS keys, WABA tokens).
/// </summary>
/// <remarks>
/// <para>
/// AES-256-GCM: authenticated, so a tampered ciphertext fails to decrypt instead of silently
/// yielding garbage. The previous scheme was AES-CBC with no MAC, which is malleable. New values
/// are written as <c>enc:v2:&lt;base64(nonce | tag | ciphertext)&gt;</c>; values written by the old
/// scheme (bare base64) still decrypt, and are upgraded the next time they are saved.
/// </para>
/// <para>
/// The key is derived from <c>Encryption:Key</c> with HKDF. There is no built-in fallback key any
/// more: a deployment without one fails at startup rather than "encrypting" with a value published
/// in the source code.
/// </para>
/// </remarks>
public sealed class EncryptionService : IEncryptionService
{
    public EncryptionService(IConfiguration configuration)
    {
        SecretCipher.EnsureInitialized(configuration);
    }

    public string Encrypt(string plainText) => SecretCipher.Encrypt(plainText);

    public string Decrypt(string cipherText) => SecretCipher.Decrypt(cipherText);
}

/// <summary>
/// The process-wide cipher. Static because EF value converters (which encrypt WABA credentials
/// transparently) are built once with the model and cannot take constructor dependencies.
/// </summary>
public static class SecretCipher
{
    public const string Prefix = "enc:v2:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static byte[]? _gcmKey;
    private static byte[]? _legacyKey;
    private static readonly object Gate = new();

    public static bool IsInitialized => _gcmKey is not null;

    public static void EnsureInitialized(IConfiguration configuration)
    {
        if (_gcmKey is not null) return;
        Initialize(configuration["Encryption:Key"]);
    }

    public static void Initialize(string? rootKey)
    {
        if (string.IsNullOrWhiteSpace(rootKey))
        {
            throw new InvalidOperationException(
                "Encryption:Key is not configured. Stored credentials cannot be protected without it. "
              + "Set it through an environment variable (Encryption__Key), user-secrets, or your secret store.");
        }

        lock (Gate)
        {
            if (_gcmKey is not null) return;

            var ikm = Encoding.UTF8.GetBytes(rootKey);
            _gcmKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 32, info: Encoding.UTF8.GetBytes("WabaConnect.FieldEncryption.v2"));

            // The old scheme's key: a bare SHA-256 of the configured string. Kept only to read
            // values written before v2.
            _legacyKey = SHA256.HashData(ikm);
        }
    }

    public static bool IsEncrypted(string? value) => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var key = _gcmKey ?? throw new InvalidOperationException("The secret cipher has not been initialised.");

        var plain = Encoding.UTF8.GetBytes(plainText);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        var tag = output.AsSpan(NonceSize, TagSize);
        var cipher = output.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        return Prefix + Convert.ToBase64String(output);
    }

    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        if (!IsEncrypted(cipherText)) return DecryptLegacy(cipherText);

        var key = _gcmKey ?? throw new InvalidOperationException("The secret cipher has not been initialised.");
        var data = Convert.FromBase64String(cipherText[Prefix.Length..]);
        if (data.Length < NonceSize + TagSize) throw new CryptographicException("Invalid ciphertext.");

        var plain = new byte[data.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(
            data.AsSpan(0, NonceSize),
            data.AsSpan(NonceSize + TagSize),
            data.AsSpan(NonceSize, TagSize),
            plain);

        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// For columns that were stored in plaintext before encryption was applied to them: an
    /// <see cref="Prefix"/>ed value is decrypted, anything else is returned as-is (and will be
    /// encrypted on its next save).
    /// </summary>
    public static string DecryptOrPassThrough(string value) =>
        IsEncrypted(value) && IsInitialized ? Decrypt(value) : value;

    /// <summary>Encrypts for storage; a no-op before initialisation (design-time tooling).</summary>
    public static string EncryptForStorage(string value) =>
        string.IsNullOrEmpty(value) || !IsInitialized || IsEncrypted(value) ? value : Encrypt(value);

    private static string DecryptLegacy(string cipherText)
    {
        var key = _legacyKey ?? throw new InvalidOperationException("The secret cipher has not been initialised.");
        var full = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = key;
        var ivLength = aes.BlockSize / 8;
        if (full.Length < ivLength) throw new CryptographicException("Invalid cipher text length.");

        // The old writer went through a StreamWriter, which may have emitted a UTF-8 byte-order
        // mark ahead of the text; the old reader stripped it, so this does too.
        return Encoding.UTF8.GetString(aes.DecryptCbc(full.AsSpan(ivLength), full.AsSpan(0, ivLength))).TrimStart('﻿');
    }
}
