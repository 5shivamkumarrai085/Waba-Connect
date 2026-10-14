using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Remittance.Tests;

/// <summary>Drives the Send Money Flow through the endpoint, step by step, the way WhatsApp would.</summary>
public static class Journey
{
    public const string AccountNumber = "1234567890";

    /// <summary>INIT, then the code the development mock shows on the OTP screen.</summary>
    public static async Task<string?> InitAsync(RemittanceHarness harness, string token)
    {
        var (_, body) = await harness.ExchangeAsync(token, "INIT");
        return DevCode(body);
    }

    public static string? DevCode(JsonObject? body)
    {
        var hint = Data(body)?["dev_hint"]?.GetValue<string>();
        return hint is null ? null : Regex.Match(hint, @"\d{6}") is { Success: true } m ? m.Value : null;
    }

    public static Task<(int Status, JsonObject? Body)> ActAsync(RemittanceHarness harness, string token, string screen, string action, JsonObject? data = null)
    {
        data ??= new JsonObject();
        // As the Flow JSON sends it: WhatsApp strips a payload field named "action".
        data["op"] = action;
        return harness.ExchangeAsync(token, "data_exchange", screen, data);
    }

    /// <summary>A verified session with an active quote for <paramref name="amount"/> on the test corridor.</summary>
    public static async Task<(int ContactId, Guid SessionId, string Token)> QuotedSessionAsync(RemittanceHarness harness, string amount = "500")
    {
        var (contactId, sessionId, token) = await harness.NewSessionAsync();
        var code = await InitAsync(harness, token) ?? throw new InvalidOperationException("The mock OTP did not expose a code.");
        await ActAsync(harness, token, "VERIFY_IDENTITY", "verify_otp", new JsonObject { ["otp"] = code });
        var (_, quoted) = await ActAsync(harness, token, "TRANSFER_DETAILS", "quote_or_continue", Transfer(harness, amount));
        if (Data(quoted)?["quote_ready"]?.GetValue<bool>() != true) throw new InvalidOperationException($"No quote: {quoted?.ToJsonString()}");
        return (contactId, sessionId, token);
    }

    /// <summary>A session taken all the way to a created order, paid by <paramref name="paymentMethod"/>.</summary>
    public static async Task<(int ContactId, Guid SessionId, string Token, string Reference)> OrderAsync(RemittanceHarness harness, string paymentMethod = "FPX", string amount = "500")
    {
        var (contactId, sessionId, token) = await QuotedSessionAsync(harness, amount);
        await ActAsync(harness, token, "TRANSFER_DETAILS", "quote_or_continue", Transfer(harness, amount));
        await ActAsync(harness, token, "BENEFICIARY", "save_beneficiary", NewBeneficiary());
        await ActAsync(harness, token, "COMPLIANCE", "save_compliance", Compliance());
        var (_, done) = await ActAsync(harness, token, "REVIEW_CONFIRM", "confirm", new JsonObject { ["payment_method"] = paymentMethod });
        var reference = Params(done)?["order_reference"]?.GetValue<string>()
                        ?? throw new InvalidOperationException($"Confirm did not complete: {done?.ToJsonString()}");
        return (contactId, sessionId, token, reference);
    }

    public static JsonObject Transfer(RemittanceHarness harness, string amount) => new()
    {
        ["country"] = harness.CorridorId.ToString(),
        ["method"] = harness.BankMethodId.ToString(),
        ["amount"] = amount
    };

    public static JsonObject NewBeneficiary(string? account = AccountNumber, string? branch = "ABCD12", string? institution = "BANKA",
        string? name = "Siti Rahma", string? relationship = "zz_rt_friend") => new()
    {
        ["beneficiary"] = "new",
        ["full_name"] = name,
        ["institution"] = institution,
        ["relationship"] = relationship,
        ["f1"] = account,
        ["f2"] = branch
    };

    public static JsonObject Compliance(string funds = "zz_rt_salary", string purpose = "zz_rt_family") => new()
    {
        ["source_of_funds"] = funds,
        ["purpose"] = purpose
    };

    public static string? Screen(JsonObject? body) => body?["screen"]?.GetValue<string>();
    public static JsonObject? Data(JsonObject? body) => body?["data"] as JsonObject;
    public static string? Error(JsonObject? body) => Data(body)?["error_message"]?.GetValue<string>();
    public static string? Text(JsonObject? body, string key) => Data(body)?[key]?.GetValue<string>();
    public static bool? Flag(JsonObject? body, string key) => Data(body)?[key]?.GetValue<bool>();
    public static JsonObject? Params(JsonObject? body) => Data(body)?["extension_message_response"]?["params"] as JsonObject;

    public static IEnumerable<string> OptionIds(JsonObject? body, string key) =>
        (Data(body)?[key] as JsonArray)?.Select(o => o?["id"]?.GetValue<string>() ?? string.Empty) ?? [];
}
