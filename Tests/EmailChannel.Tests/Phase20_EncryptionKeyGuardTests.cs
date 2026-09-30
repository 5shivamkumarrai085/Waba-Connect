using System.Security.Cryptography;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Services;

namespace EmailChannel.Tests;

/// <summary>
/// Phase 20 — the startup key guard. A process whose Encryption:Key cannot read the stored
/// credentials must refuse to start, instead of dead-lettering sends one by one (2026-09-28).
/// </summary>
public static class Phase20_EncryptionKeyGuardTests
{
    public static async Task RunAsync(TestHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 20 — encryption key guard");

        run.Section("The decision");
        var sample = SecretCipher.Encrypt("smtp-password-sample");
        run.Check("the right key passes", EncryptionKeyGuard.KeyMatches([sample], SecretCipher.Decrypt));

        // A cipher with an unrelated key, the way a process started with a stand-in key would decrypt.
        var otherKey = RandomNumberGenerator.GetBytes(32);
        string WrongKeyDecrypt(string value)
        {
            var data = Convert.FromBase64String(value[SecretCipher.Prefix.Length..]);
            var plain = new byte[data.Length - 28];
            using var gcm = new AesGcm(otherKey, 16);
            gcm.Decrypt(data.AsSpan(0, 12), data.AsSpan(28), data.AsSpan(12, 16), plain);
            return System.Text.Encoding.UTF8.GetString(plain);
        }
        run.Check("a wrong key is refused", !EncryptionKeyGuard.KeyMatches([sample, SecretCipher.Encrypt("another")], WrongKeyDecrypt));
        run.Check("one corrupt row does not block a correct key",
            EncryptionKeyGuard.KeyMatches([SecretCipher.Prefix + "AAAA", sample], SecretCipher.Decrypt));
        run.Check("an empty database has nothing to check", EncryptionKeyGuard.KeyMatches([], WrongKeyDecrypt));

        run.Section("Against the real database");
        Exception? failure = null;
        try { await harness.InScopeAsync(services => EncryptionKeyGuard.VerifyAsync(services)); }
        catch (Exception ex) { failure = ex; }
        run.Check("the configured key reads the stored credentials", failure is null, failure?.Message);
    }
}
