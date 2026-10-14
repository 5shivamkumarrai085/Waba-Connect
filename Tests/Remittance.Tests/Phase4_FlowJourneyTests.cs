using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Enums;
using WhatsAppCampaignApi.Services.Remittance;
using WhatsAppCampaignApi.Services.WhatsApp.Flows;
using static Remittance.Tests.Journey;

namespace Remittance.Tests;

/// <summary>
/// The whole customer journey through the data-exchange endpoint: verification enforced on the
/// server, options built from configuration, recipient validation, compliance, review, confirm.
/// </summary>
public static class Phase4_FlowJourneyTests
{
    public static async Task RunAsync(RemittanceHarness harness, TestRun run)
    {
        run.BeginPhase("Phase 4 — Flow journey (server-authoritative)");

        try
        {
            var (contactId, sessionId, token) = await harness.NewSessionAsync();

            run.Section("Step 1 — verify mobile");
            var (initStatus, init) = await harness.ExchangeAsync(token, "INIT");
            run.Check("INIT opens the verification screen", initStatus == 200 && Screen(init) == SendMoneyFlowHandler.ScreenVerify, init?.ToJsonString());
            run.Check("the registered WhatsApp number is shown", Text(init, "phone_display")?.StartsWith("+6099") == true, Text(init, "phone_display"));
            var code = DevCode(init);
            run.Check("the development hint carries the mock code", code is not null, Text(init, "dev_hint"));

            var (_, skipped) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "500"));
            run.Check("asking for a quote before verifying is refused", Screen(skipped) == SendMoneyFlowHandler.ScreenVerify && Error(skipped) is not null, Error(skipped));
            var (_, skippedConfirm) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenReview, "confirm", new JsonObject { ["payment_method"] = "FPX", ["verified"] = true });
            run.Check("confirming before verifying is refused, whatever the Flow claims", Screen(skippedConfirm) == SendMoneyFlowHandler.ScreenVerify);

            var (_, wrong) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenVerify, "verify_otp", new JsonObject { ["otp"] = "000000" == code ? "111111" : "000000" });
            run.Check("a wrong code stays on verification with a message", Screen(wrong) == SendMoneyFlowHandler.ScreenVerify && Error(wrong)?.Contains("attempt") == true, Error(wrong));

            var (_, verified) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenVerify, "verify_otp", new JsonObject { ["otp"] = code });
            run.Check("the right code moves on to transfer details", Screen(verified) == SendMoneyFlowHandler.ScreenTransfer, Error(verified));
            var verifiedAt = await harness.ScalarAsync("SELECT \"OtpVerifiedAt\" IS NOT NULL FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", sessionId);
            run.Check("verification is recorded on the server-side session", verifiedAt is true);

            run.Section("Step 2 — country, method, amount (all from configuration)");
            run.Check("our corridor is offered as a destination", OptionIds(verified, "countries").Contains(harness.CorridorId.ToString()));
            var (_, country) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "select_country", new JsonObject { ["country"] = harness.CorridorId.ToString() });
            var methods = OptionIds(country, "payout_methods").ToList();
            run.Check("choosing the country offers its active payout method", methods.Contains(harness.BankMethodId.ToString()), string.Join(",", methods));
            run.Check("inactive payout methods are not offered", !methods.Contains(harness.InactiveMethodId.ToString()));
            run.Check("limits come from the corridor", Text(country, "limits_text") == "Send MYR 20.00 to MYR 20,000.00", Text(country, "limits_text"));
            run.Check("no quote is shown before Calculate", Flag(country, "quote_ready") == false && Text(country, "footer_label") == "Calculate");

            var (_, tooSmall) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "10"));
            run.Check("an amount under the minimum is refused with the limits", Error(tooSmall)?.Contains("between") == true, Error(tooSmall));
            var (_, notANumber) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "lots"));
            run.Check("a non-numeric amount is refused", Error(notANumber) is not null && Flag(notANumber, "quote_ready") == false, Error(notANumber));
            var (_, wrongMethod) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue",
                new JsonObject { ["country"] = harness.CorridorId.ToString(), ["method"] = harness.InactiveMethodId.ToString(), ["amount"] = "500" });
            run.Check("a crafted request for an inactive method is refused", Error(wrongMethod) is not null && Flag(wrongMethod, "quote_ready") == false, Error(wrongMethod));

            var (_, quoted) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "500"));
            run.Check("Calculate shows the guaranteed quote", Flag(quoted, "quote_ready") == true, Error(quoted));
            run.Check("rate line: 1 MYR = 3,585.4 IDR", Text(quoted, "rate_line") == "Guaranteed rate: 1 MYR = 3,585.4 IDR", Text(quoted, "rate_line"));
            run.Check("fee line shows the promotional fee", Text(quoted, "fee_line") == "Transfer fee: MYR 0.00 (PROMO)", Text(quoted, "fee_line"));
            run.Check("recipient gets IDR 1,792,700", Text(quoted, "receive_line") == "Recipient gets: IDR 1,792,700", Text(quoted, "receive_line"));
            run.Check("total to pay MYR 500.00", Text(quoted, "total_line") == "Total to pay: MYR 500.00", Text(quoted, "total_line"));
            run.Check("the footer now continues", Text(quoted, "footer_label") == "Select beneficiary");

            run.Section("Step 3 — beneficiary (dynamic fields)");
            var (_, beneficiary) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "500"));
            run.Check("continuing on a valid quote opens the beneficiary screen", Screen(beneficiary) == SendMoneyFlowHandler.ScreenBeneficiary, Error(beneficiary));
            run.Check("a new customer starts on \"Add a new recipient\"", Text(beneficiary, "selected_beneficiary") == "new");
            run.Check("slot 1 is the configured \"Account number\", required",
                Text(beneficiary, "f1_label") == "Account number" && Flag(beneficiary, "f1_visible") == true && Flag(beneficiary, "f1_required") == true);
            run.Check("slot 2 is \"Branch code\" with its helper text", Text(beneficiary, "f2_label") == "Branch code" && Text(beneficiary, "f2_helper") == "e.g. ABCD12");
            run.Check("the inactive field and spare slots are hidden",
                Enumerable.Range(3, 4).All(n => Flag(beneficiary, $"f{n}_visible") == false));
            run.Check("slot max length follows the field (12)", Data(beneficiary)?["f1_max"]?.GetValue<int>() == 12);
            var banks = OptionIds(beneficiary, "institutions").ToList();
            run.Check("active banks are offered, the closed one is not", banks.SequenceEqual(["BANKA", "BANKB"]), string.Join(",", banks));

            (string Name, JsonObject Data, string Expect)[] invalid =
            [
                ("missing name", NewBeneficiary(name: null), "full name"),
                ("missing required account number", NewBeneficiary(account: null), "Account number is required"),
                ("account number too short", NewBeneficiary(account: "123"), "at least 6"),
                ("letters in a digits-only field", NewBeneficiary(account: "12345abc"), "digits only"),
                ("branch code not matching the pattern", NewBeneficiary(branch: "abcd12"), "expected format"),
                ("a closed bank", NewBeneficiary(institution: "OLD"), "bank"),
                ("an unknown relationship", NewBeneficiary(relationship: "boss"), "know the recipient")
            ];
            foreach (var (name, data, expect) in invalid)
            {
                var (_, rejected) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenBeneficiary, "save_beneficiary", data);
                run.Check($"rejected: {name}", Screen(rejected) == SendMoneyFlowHandler.ScreenBeneficiary && Error(rejected)?.Contains(expect) == true, Error(rejected));
            }

            var (_, compliance) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenBeneficiary, "save_beneficiary", NewBeneficiary());
            run.Check("a valid recipient is saved and step 4 opens", Screen(compliance) == SendMoneyFlowHandler.ScreenCompliance, Error(compliance));

            await using (var scope = harness.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var saved = await db.RemittanceBeneficiaries.AsNoTracking().FirstAsync(b => b.ContactId == contactId);
                run.Check("only the masked account is kept in clear", saved.MaskedAccount == "****7890", saved.MaskedAccount);
                var details = scope.ServiceProvider.GetRequiredService<IBeneficiaryService>().ReadDetails(saved);
                run.Check("details decrypt to the typed values", details.GetValueOrDefault("account_number") == AccountNumber && details.GetValueOrDefault("branch_code") == "ABCD12");
            }
            var rawDetails = (string?)await harness.ScalarAsync("SELECT \"DetailsJson\" FROM \"RemittanceBeneficiaries\" WHERE \"ContactId\" = @p0", contactId);
            run.Check("recipient details are encrypted at rest", rawDetails?.StartsWith("enc:") == true && !rawDetails.Contains(AccountNumber), rawDetails?[..Math.Min(12, rawDetails.Length)]);

            run.Section("Step 4 — regulatory details");
            run.Check("source-of-funds and purpose lists come from configuration",
                OptionIds(compliance, "source_of_funds").Contains("zz_rt_salary") && OptionIds(compliance, "purposes").Contains("zz_rt_family"));
            var (_, badPurpose) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenCompliance, "save_compliance", Compliance(purpose: "not_a_purpose"));
            run.Check("an unknown purpose is refused", Screen(badPurpose) == SendMoneyFlowHandler.ScreenCompliance && Error(badPurpose) is not null, Error(badPurpose));
            var (_, review) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenCompliance, "save_compliance", Compliance());
            run.Check("valid answers open the review", Screen(review) == SendMoneyFlowHandler.ScreenReview, Error(review));

            run.Section("Step 5 — review and confirm");
            run.Check("review shows the recipient and masked account",
                Text(review, "recipient_line") == "Recipient: Siti Rahma" && Text(review, "bank_line") == "Bank & account: zz Bank A · ****7890", Text(review, "bank_line"));
            run.Check("review shows the total payable", Text(review, "total_line") == "Total payable: MYR 500.00", Text(review, "total_line"));
            var payments = Data(review)?["payment_methods"] as JsonArray;
            run.Check("payment methods come from configuration, with badges",
                payments?.Any(p => p?["id"]?.GetValue<string>() == "FPX" && p["metadata"]?.GetValue<string>() == "INSTANT") == true
                && payments.Any(p => p?["id"]?.GetValue<string>() == "COUNTER"));

            var (_, noMethod) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenReview, "confirm", new JsonObject { ["payment_method"] = "CARD_PASSWORD" });
            run.Check("an unconfigured payment method is refused", Screen(noMethod) == SendMoneyFlowHandler.ScreenReview && Error(noMethod) is not null, Error(noMethod));

            var oldQuote = await harness.ScalarAsync("SELECT \"QuoteId\" FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", sessionId);
            await harness.ExecAsync("UPDATE \"FxQuotes\" SET \"ExpiresAt\" = now() - interval '1 minute' WHERE \"SessionId\" = @p0", sessionId);
            var (_, requoted) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenReview, "confirm", new JsonObject { ["payment_method"] = "FPX" });
            var newQuote = await harness.ScalarAsync("SELECT \"QuoteId\" FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", sessionId);
            run.Check("confirming on an expired quote re-prices instead of ordering",
                Screen(requoted) == SendMoneyFlowHandler.ScreenReview && Flag(requoted, "notice_visible") == true && !Equals(oldQuote, newQuote), Text(requoted, "notice_text"));
            run.Check("no order exists yet", Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"TransferOrders\" WHERE \"SessionId\" = @p0", sessionId)) == 0);

            var (doneStatus, done) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenReview, "confirm", new JsonObject { ["payment_method"] = "FPX" });
            var reference = Params(done)?["order_reference"]?.GetValue<string>();
            run.Check("confirm closes the Flow with the order reference", doneStatus == 200 && Screen(done) == "SUCCESS" && reference is not null && Regex.IsMatch(reference, @"^IR-ZZ\d{6}[0-9A-HJKMNP-TV-Z]{8}$"), reference);
            run.Check("the completion carries the flow token for the webhook", Params(done)?["flow_token"]?.GetValue<string>() == token);

            await using (var scope = harness.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var order = await db.TransferOrders.AsNoTracking().FirstAsync(o => o.SessionId == sessionId);
                run.Check("the order awaits payment — confirming never marks it paid",
                    order.OrderStatus == TransferOrderStatus.AwaitingPayment && order.PaymentStatus == TransferPaymentStatus.Pending && order.PaidAt is null,
                    $"{order.OrderStatus}/{order.PaymentStatus}");
                run.Check("the order keeps the confirmed numbers", order.TotalPayable == 500m && order.ReceiveAmount == 1_792_700m && order.AppliedRate == 3585.4m);
                run.Check("the payment was handed to the provider's hosted page",
                    order.ProviderPaymentId?.StartsWith("mockpay_") == true && order.PaymentUrl?.StartsWith("https://example.test/api/dev/mock-payments/") == true, order.PaymentUrl);
                run.Check("the confirmed quote is marked used", await db.FxQuotes.AsNoTracking().AnyAsync(q => q.Id == order.QuoteId && q.Status == FxQuoteStatus.Used));
                run.Check("compliance answers are on the order", order.SourceOfFundsCode == "zz_rt_salary" && order.PurposeCode == "zz_rt_family");
            }

            var (_, again) = await ActAsync(harness, token, SendMoneyFlowHandler.ScreenReview, "confirm", new JsonObject { ["payment_method"] = "FPX" });
            run.Check("a second Confirm returns the same order", Params(again)?["order_reference"]?.GetValue<string>() == reference);
            run.Check("exactly one order exists for the session", Convert.ToInt32(await harness.ScalarAsync("SELECT count(*) FROM \"TransferOrders\" WHERE \"SessionId\" = @p0", sessionId)) == 1);
            var (reopenStatus, _) = await harness.ExchangeAsync(token, "INIT");
            run.Check("the finished Flow cannot be reopened (427)", reopenStatus == 427, reopenStatus.ToString());

            run.Section("Order created message");
            var sentBefore = harness.WhatsApp.Sent.Count;
            await harness.InScopeAsync(async sp =>
            {
                await sp.GetRequiredService<IWhatsAppFlowCompletionHandler>().HandleAsync("6099", harness.ConnectionId,
                    new JsonObject { ["flow_token"] = token, ["order_reference"] = "IR-ZZ000000" }.ToJsonString(), CancellationToken.None);
                return 0;
            });
            run.Check("a completion naming another order sends nothing", harness.WhatsApp.Sent.Count == sentBefore);

            for (var i = 0; i < 2; i++)
            {
                await harness.InScopeAsync(async sp =>
                {
                    await sp.GetRequiredService<IWhatsAppFlowCompletionHandler>().HandleAsync("6099", harness.ConnectionId,
                        new JsonObject { ["flow_token"] = token, ["order_reference"] = reference }.ToJsonString(), CancellationToken.None);
                    return 0;
                });
            }
            var sent = harness.WhatsApp.Sent.Skip(sentBefore).ToList();
            run.Check("the completion webhook sends one \"Order created\" message, even twice", sent.Count == 1, sent.Count.ToString());
            if (sent.Count > 0)
            {
                var payload = JsonNode.Parse(sent[0].Payload);
                run.Check("it is a pay button to the hosted page, not a payment form",
                    payload?["interactive"]?["type"]?.GetValue<string>() == "cta_url"
                    && payload["interactive"]?["action"]?["parameters"]?["display_text"]?.GetValue<string>() == "Pay MYR 500.00", sent[0].Payload);
            }

            run.Section("Security of the session");
            var (_, otherSessionId, otherToken) = await QuotedSessionAsync(harness);
            await ActAsync(harness, otherToken, SendMoneyFlowHandler.ScreenTransfer, "quote_or_continue", Transfer(harness, "500"));
            var firstBeneficiaryId = await harness.ScalarAsync("SELECT \"Id\" FROM \"RemittanceBeneficiaries\" WHERE \"ContactId\" = @p0", contactId);
            var (_, stolen) = await ActAsync(harness, otherToken, SendMoneyFlowHandler.ScreenBeneficiary, "save_beneficiary",
                new JsonObject { ["beneficiary"] = firstBeneficiaryId?.ToString() });
            run.Check("another customer's saved recipient cannot be used", Screen(stolen) == SendMoneyFlowHandler.ScreenBeneficiary && Error(stolen) is not null, Error(stolen));
            run.Check("…and was never attached to the session",
                await harness.ScalarAsync("SELECT \"BeneficiaryId\" FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", otherSessionId) is null);

            var (_, lockedSessionId, lockedToken) = await harness.NewSessionAsync();
            var lockCode = await InitAsync(harness, lockedToken);
            for (var i = 0; i < 5; i++)
                await ActAsync(harness, lockedToken, SendMoneyFlowHandler.ScreenVerify, "verify_otp", new JsonObject { ["otp"] = lockCode == "000000" ? "111111" : "000000" });
            var (lockedStatus, _) = await ActAsync(harness, lockedToken, SendMoneyFlowHandler.ScreenVerify, "verify_otp", new JsonObject { ["otp"] = lockCode });
            run.Check("five wrong codes lock the session; the right code then gets 427", lockedStatus == 427, lockedStatus.ToString());
            run.Check("the session is marked Locked",
                (string?)await harness.ScalarAsync("SELECT \"Status\" FROM \"RemittanceSessions\" WHERE \"Id\" = @p0", lockedSessionId) == "Locked");

            await harness.ExecAsync("UPDATE \"RemittanceSessions\" SET \"ExpiresAt\" = now() - interval '1 minute' WHERE \"Id\" = @p0", otherSessionId);
            var (expiredStatus, _) = await harness.ExchangeAsync(otherToken, "BACK", SendMoneyFlowHandler.ScreenTransfer);
            run.Check("an expired session gets 427", expiredStatus == 427, expiredStatus.ToString());

            run.Section("Configuration guards (what the Flow can render)");
            await AdminGuardsAsync(harness, run);
        }
        catch (Exception ex)
        {
            run.Error("Phase 4 aborted", ex);
        }
    }

    private static async Task AdminGuardsAsync(RemittanceHarness harness, TestRun run)
    {
        await using var scope = harness.CreateScope();
        var admin = scope.ServiceProvider.GetRequiredService<IRemittanceAdminService>();
        var added = new List<int>();

        static SaveBeneficiaryFieldRequest Field(string key, string label, string? pattern = null) =>
            new(key, label, RemittanceFieldInputType.Text, false, null, 40, pattern, null, false, true, 10);

        try
        {
            run.Check("a label over 20 characters is refused",
                await ThrowsAsync<ArgumentException>(() => admin.SaveFieldAsync(harness.BankMethodId, null, Field("long_label", "Beneficiary account no."), CancellationToken.None)));
            run.Check("an invalid pattern is refused",
                await ThrowsAsync<ArgumentException>(() => admin.SaveFieldAsync(harness.BankMethodId, null, Field("bad_pattern", "Code", "[unclosed"), CancellationToken.None)));

            for (var i = 3; i <= 6; i++)
            {
                var dto = await admin.SaveFieldAsync(harness.BankMethodId, null, Field($"zz_extra_{i}", $"Extra {i}"), CancellationToken.None);
                added.Add(dto.Id);
            }
            run.Check("up to six active fields are accepted", added.Count == 4);
            run.Check("a seventh active field is refused (the Flow has six slots)",
                await ThrowsAsync<ArgumentException>(() => admin.SaveFieldAsync(harness.BankMethodId, null, Field("zz_extra_7", "Extra 7"), CancellationToken.None)));
        }
        finally
        {
            foreach (var id in added) await admin.DeleteFieldAsync(id, CancellationToken.None);
        }
    }

    private static async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); return false; }
        catch (T) { return true; }
    }
}
