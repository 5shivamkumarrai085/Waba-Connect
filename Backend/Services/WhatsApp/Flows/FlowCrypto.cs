using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace WhatsAppCampaignApi.Services.WhatsApp.Flows;

/// <summary>
/// WhatsApp Flows data-exchange encryption, as Meta specifies it:
/// <list type="number">
/// <item>Meta wraps a fresh 128-bit AES key with our RSA public key (RSA-OAEP, SHA-256, MGF1-SHA-256).</item>
/// <item>The request JSON is AES-GCM encrypted with that key and a 16-byte IV; the 16-byte tag is appended.</item>
/// <item>The response is AES-GCM encrypted with the same key and the IV with every bit flipped, then Base64.</item>
/// </list>
/// BouncyCastle does the AES-GCM because .NET's AesGcm only accepts 12-byte nonces.
/// </summary>
public static class FlowCrypto
{
    private const int TagBits = 128;

    public sealed record DecryptedRequest(string Json, byte[] AesKey, byte[] Iv);

    /// <summary>Thrown when a request cannot be decrypted — the endpoint answers 421 so Meta refetches our key.</summary>
    public sealed class FlowDecryptionException(string message, Exception? inner = null) : Exception(message, inner);

    public static DecryptedRequest DecryptRequest(string encryptedFlowData, string encryptedAesKey, string initialVector, RSA privateKey)
    {
        try
        {
            var aesKey = privateKey.Decrypt(Convert.FromBase64String(encryptedAesKey), RSAEncryptionPadding.OaepSHA256);
            var iv = Convert.FromBase64String(initialVector);
            var plaintext = AesGcm(forEncryption: false, aesKey, iv, Convert.FromBase64String(encryptedFlowData));
            return new DecryptedRequest(Encoding.UTF8.GetString(plaintext), aesKey, iv);
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidCipherTextException or FormatException or ArgumentException)
        {
            throw new FlowDecryptionException("The request could not be decrypted with this connection's Flows key.", ex);
        }
    }

    /// <summary>Encrypts a response body for Meta: same key, flipped IV, Base64.</summary>
    public static string EncryptResponse(string json, byte[] aesKey, byte[] requestIv) =>
        Convert.ToBase64String(AesGcm(forEncryption: true, aesKey, Flip(requestIv), Encoding.UTF8.GetBytes(json)));

    // ── What Meta does — used by the tests and the development helpers to build real requests ──

    public sealed record EncryptedRequest(string EncryptedFlowData, string EncryptedAesKey, string InitialVector, byte[] AesKey, byte[] Iv);

    public static EncryptedRequest EncryptRequestLikeMeta(string json, RSA publicKey)
    {
        var aesKey = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var data = AesGcm(forEncryption: true, aesKey, iv, Encoding.UTF8.GetBytes(json));
        var wrapped = publicKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);
        return new EncryptedRequest(Convert.ToBase64String(data), Convert.ToBase64String(wrapped), Convert.ToBase64String(iv), aesKey, iv);
    }

    public static string DecryptResponseLikeMeta(string base64Response, byte[] aesKey, byte[] requestIv) =>
        Encoding.UTF8.GetString(AesGcm(forEncryption: false, aesKey, Flip(requestIv), Convert.FromBase64String(base64Response)));

    // ── Key material ─────────────────────────────────────────────────────────────────────────

    public sealed record KeyPair(string PublicKeyPem, string PrivateKeyPem);

    /// <summary>A new RSA-2048 pair: SubjectPublicKeyInfo PEM to upload, PKCS#8 PEM to keep.</summary>
    public static KeyPair GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        return new KeyPair(rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    public static RSA LoadKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static byte[] Flip(byte[] iv)
    {
        var flipped = new byte[iv.Length];
        for (var i = 0; i < iv.Length; i++) flipped[i] = (byte)~iv[i];
        return flipped;
    }

    private static byte[] AesGcm(bool forEncryption, byte[] key, byte[] iv, byte[] input)
    {
        var cipher = new GcmBlockCipher(new AesEngine());
        cipher.Init(forEncryption, new AeadParameters(new KeyParameter(key), TagBits, iv));
        var output = new byte[cipher.GetOutputSize(input.Length)];
        var length = cipher.ProcessBytes(input, 0, input.Length, output, 0);
        length += cipher.DoFinal(output, length);
        return length == output.Length ? output : output[..length];
    }
}
