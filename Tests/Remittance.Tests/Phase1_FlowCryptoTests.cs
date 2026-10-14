using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;

namespace Remittance.Tests;

/// <summary>
/// The WhatsApp Flows endpoint protocol: Meta's encryption scheme round-tripped exactly, and the
/// HTTP codes Meta acts on (421 refetch key, 432 signature, 427 dead token).
/// </summary>
public static class Phase1_FlowCryptoTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 1 — Flow encryption and endpoint protocol");

        try
        {
            run.Section("Encryption, as Meta specifies it");
            var pair = FlowCrypto.GenerateKeyPair();
            using var publicKey = FlowCrypto.LoadKey(pair.PublicKeyPem);
            using var privateKey = FlowCrypto.LoadKey(pair.PrivateKeyPem);

            var json = "{\"version\":\"3.0\",\"action\":\"ping\"}";
            var request = FlowCrypto.EncryptRequestLikeMeta(json, publicKey);
            run.Check("the IV is 16 bytes (Meta's size, which .NET's AesGcm cannot use)", Convert.FromBase64String(request.InitialVector).Length == 16);
            var decrypted = FlowCrypto.DecryptRequest(request.EncryptedFlowData, request.EncryptedAesKey, request.InitialVector, privateKey);
            run.Check("a Meta-style request decrypts to the original JSON", decrypted.Json == json, decrypted.Json);

            var response = FlowCrypto.EncryptResponse("{\"data\":{\"status\":\"active\"}}", decrypted.AesKey, decrypted.Iv);
            run.Check("the response decrypts with the bit-flipped IV",
                FlowCrypto.DecryptResponseLikeMeta(response, request.AesKey, request.Iv) == "{\"data\":{\"status\":\"active\"}}");

            var wrongIvFails = false;
            try { FlowCrypto.DecryptResponseLikeMeta(response, request.AesKey, FlipBack(request.Iv)); }
            catch (Exception) { wrongIvFails = true; }
            run.Check("decrypting the response with the un-flipped IV fails (GCM tag check)", wrongIvFails);

            var other = FlowCrypto.GenerateKeyPair();
            using var otherPublic = FlowCrypto.LoadKey(other.PublicKeyPem);
            var foreign = FlowCrypto.EncryptRequestLikeMeta(json, otherPublic);
            var foreignFails = false;
            try { FlowCrypto.DecryptRequest(foreign.EncryptedFlowData, foreign.EncryptedAesKey, foreign.InitialVector, privateKey); }
            catch (FlowCrypto.FlowDecryptionException) { foreignFails = true; }
            run.Check("a request encrypted to another key is refused", foreignFails);

            run.Section("The encrypted endpoint");
            await using var scope = harness.CreateScope();
            var endpoint = scope.ServiceProvider.GetRequiredService<IWhatsAppFlowEndpoint>();
            using var connectionKey = FlowCrypto.LoadKey(harness.PublicKeyPem);

            var ping = Envelope(FlowCrypto.EncryptRequestLikeMeta(json, connectionKey), out var pingRequest);
            var pingResult = await endpoint.HandleEncryptedAsync(harness.EndpointKey, ping, Sign(ping), CancellationToken.None);
            var pingBody = pingResult.Body is null ? null : FlowCrypto.DecryptResponseLikeMeta(pingResult.Body, pingRequest.AesKey, pingRequest.Iv);
            run.Check("ping answers 200 with {\"data\":{\"status\":\"active\"}}", pingResult.StatusCode == 200 && pingBody == "{\"data\":{\"status\":\"active\"}}", pingBody);
            run.Check("ping is answered as text/plain Base64", pingResult.ContentType == "text/plain");
            run.Check("the health check is recorded",
                await harness.ScalarAsync("SELECT \"LastPingAt\" IS NOT NULL FROM \"WhatsAppFlowRegistrations\" WHERE \"EndpointKey\" = @p0", harness.EndpointKey) is true);

            var wrongKeyBody = Envelope(FlowCrypto.EncryptRequestLikeMeta(json, otherPublic), out _);
            var wrongKey = await endpoint.HandleEncryptedAsync(harness.EndpointKey, wrongKeyBody, Sign(wrongKeyBody), CancellationToken.None);
            run.Check("undecryptable body → 421 (WhatsApp refetches the public key)", wrongKey.StatusCode == 421, wrongKey.StatusCode.ToString());

            var badSignature = await endpoint.HandleEncryptedAsync(harness.EndpointKey, ping, "sha256=" + new string('0', 64), CancellationToken.None);
            run.Check("wrong X-Hub-Signature-256 → 432", badSignature.StatusCode == 432, badSignature.StatusCode.ToString());

            var unsigned = await endpoint.HandleEncryptedAsync(harness.EndpointKey, ping, null, CancellationToken.None);
            run.Check("a missing signature is accepted in Development only (as the messages webhook does)", unsigned.StatusCode == 200, unsigned.StatusCode.ToString());

            var unknown = await endpoint.HandleEncryptedAsync("no-such-key", ping, Sign(ping), CancellationToken.None);
            run.Check("unknown endpoint key → 404", unknown.StatusCode == 404, unknown.StatusCode.ToString());

            run.Section("Dispatch");
            var (status, body) = await endpoint.HandlePlaintextAsync(harness.EndpointKey, new JsonObject
            {
                ["version"] = "3.0", ["action"] = "data_exchange", ["flow_token"] = "sm_unknown",
                ["data"] = new JsonObject { ["error"] = "INVALID_SCREEN", ["error_message"] = "bad data" }
            }, CancellationToken.None);
            run.Check("an error notification is acknowledged", status == 200 && body?["data"]?["acknowledged"]?.GetValue<bool>() == true, body?.ToJsonString());

            var (deadStatus, _) = await endpoint.HandlePlaintextAsync(harness.EndpointKey, new JsonObject
            {
                ["version"] = "3.0", ["action"] = "INIT", ["flow_token"] = "sm_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            }, CancellationToken.None);
            run.Check("an unknown flow token → 427", deadStatus == 427, deadStatus.ToString());

            var (prefixStatus, _) = await endpoint.HandlePlaintextAsync(harness.EndpointKey, new JsonObject
            {
                ["version"] = "3.0", ["action"] = "INIT", ["flow_token"] = "xx_nobody"
            }, CancellationToken.None);
            run.Check("a token for no known handler → 427", prefixStatus == 427, prefixStatus.ToString());
        }
        catch (Exception ex)
        {
            run.Error("Phase 1 aborted", ex);
        }
    }

    private static byte[] Envelope(FlowCrypto.EncryptedRequest encrypted, out FlowCrypto.EncryptedRequest kept)
    {
        kept = encrypted;
        return Encoding.UTF8.GetBytes(new JsonObject
        {
            ["encrypted_flow_data"] = encrypted.EncryptedFlowData,
            ["encrypted_aes_key"] = encrypted.EncryptedAesKey,
            ["initial_vector"] = encrypted.InitialVector
        }.ToJsonString());
    }

    private static string Sign(byte[] body) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(RemittanceHarness.AppSecret), body)).ToLowerInvariant();

    /// <summary>Pre-flipped, so DecryptResponseLikeMeta's own flip lands back on the request IV — the wrong one.</summary>
    private static byte[] FlipBack(byte[] iv) => iv.Select(b => (byte)~b).ToArray();
}
