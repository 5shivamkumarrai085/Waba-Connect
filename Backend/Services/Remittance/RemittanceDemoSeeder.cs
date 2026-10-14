using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>
/// DEVELOPMENT DEMO DATA for Send Money: Malaysia (MYR) to six countries, matching the reference
/// screens. Inserted only through the Development-only seed endpoint, never by a migration — on a
/// real deployment this is configuration the business enters under Setup › Send Money. Idempotent:
/// rows that already exist (by their natural keys) are left exactly as they are.
/// </summary>
public static class RemittanceDemoSeeder
{
    private sealed record Field(string Key, string Label, RemittanceFieldInputType Type, int? Min, int? Max, string? Pattern, string? Helper, bool Account);
    private sealed record Method(string Code, string Name, string Eta, decimal Fee, decimal Percent, Field[] Fields, (string Code, string Name)[] Institutions);
    private sealed record Route(string Country, string Name, string Currency, int Decimals, int MarginBps, Method[] Methods);

    private static Field Account(int min, int max) => new("account_number", "Account number", RemittanceFieldInputType.Number, min, max, null, $"{min}–{max} digits", true);
    private static readonly Field Wallet = new("wallet_number", "Wallet mobile no.", RemittanceFieldInputType.Phone, null, 20, null, "Number linked to the wallet", true);

    private static readonly Route[] Routes =
    [
        new("ID", "Indonesia", "IDR", 0, 150,
        [
            new("BANK_TRANSFER", "Bank Transfer", "Instant / within 15 mins", 4m, 0m, [Account(6, 20)],
                [("BCA", "Bank Central Asia (BCA)"), ("BRI", "Bank Rakyat Indonesia (BRI)"), ("MANDIRI", "Bank Mandiri"), ("BNI", "Bank Negara Indonesia (BNI)")]),
            new("EWALLET", "E-wallet", "Instant", 3m, 0m, [Wallet], [("GOPAY", "GoPay"), ("OVO", "OVO"), ("DANA", "DANA")])
        ]),
        new("PH", "Philippines", "PHP", 2, 150,
        [
            new("BANK_TRANSFER", "Bank Transfer", "Within 1 hour", 4m, 0m, [Account(8, 20)], [("BDO", "BDO Unibank"), ("BPI", "BPI"), ("METROBANK", "Metrobank")]),
            new("EWALLET", "E-wallet", "Instant", 3m, 0m, [Wallet], [("GCASH", "GCash"), ("MAYA", "Maya")]),
            new("CASH_PICKUP", "Cash Pickup", "Ready in 30 mins", 6m, 0.5m,
                [new("recipient_mobile", "Recipient mobile", RemittanceFieldInputType.Phone, null, 20, null, "We text the pickup code here", true)],
                [("CEBUANA", "Cebuana Lhuillier"), ("MLHUILLIER", "M Lhuillier")])
        ]),
        new("IN", "India", "INR", 2, 120,
        [
            // No bank list: the IFSC code identifies the branch, which shows the "no institution" path.
            new("BANK_TRANSFER", "Bank Transfer (IMPS)", "Instant", 4m, 0m,
                [Account(9, 18), new("ifsc", "IFSC code", RemittanceFieldInputType.Text, 11, 11, "[A-Z]{4}0[A-Z0-9]{6}", "11 characters, e.g. HDFC0001234", false)],
                [])
        ]),
        new("BD", "Bangladesh", "BDT", 2, 180,
        [
            new("BANK_TRANSFER", "Bank Transfer", "Within 1 day", 5m, 0m,
                [Account(10, 17), new("routing_number", "Routing number", RemittanceFieldInputType.Number, 9, 9, null, "9 digits", false)],
                [("DBBL", "Dutch-Bangla Bank"), ("BRAC", "BRAC Bank"), ("SONALI", "Sonali Bank")]),
            new("EWALLET", "Mobile wallet", "Instant", 3m, 0m, [Wallet], [("BKASH", "bKash"), ("NAGAD", "Nagad")])
        ]),
        new("SG", "Singapore", "SGD", 2, 80,
        [
            new("BANK_TRANSFER", "Bank Transfer", "Instant (FAST)", 4m, 0m, [Account(7, 14)], [("DBS", "DBS / POSB"), ("OCBC", "OCBC"), ("UOB", "UOB")])
        ]),
        new("VN", "Vietnam", "VND", 0, 150,
        [
            new("BANK_TRANSFER", "Bank Transfer", "Within 1 hour", 4m, 0m, [Account(8, 19)], [("VCB", "Vietcombank"), ("TCB", "Techcombank"), ("BIDV", "BIDV")])
        ])
    ];

    private static readonly (string Category, string Code, string Label)[] Lookups =
    [
        (RemittanceLookup.SourceOfFunds, "salary", "Salary / Employment Income"),
        (RemittanceLookup.SourceOfFunds, "savings", "Personal Savings / Deposits"),
        (RemittanceLookup.SourceOfFunds, "business", "Business Income / Profits"),
        (RemittanceLookup.SourceOfFunds, "investment", "Investment Returns"),
        (RemittanceLookup.SourceOfFunds, "gift", "Allowance / Gift"),
        (RemittanceLookup.Purpose, "family_support", "Family Maintenance"),
        (RemittanceLookup.Purpose, "education", "Education / School Fees"),
        (RemittanceLookup.Purpose, "medical", "Medical / Healthcare"),
        (RemittanceLookup.Purpose, "savings_home", "Savings in Home Country"),
        (RemittanceLookup.Purpose, "loan_repayment", "Loan / Mortgage Repayment"),
        (RemittanceLookup.Purpose, "goods_services", "Payment for Goods / Services"),
        (RemittanceLookup.Relationship, "family_spouse", "Family / Spouse"),
        (RemittanceLookup.Relationship, "relative", "Relative"),
        (RemittanceLookup.Relationship, "friend", "Friend"),
        (RemittanceLookup.Relationship, "business_partner", "Business Partner"),
        (RemittanceLookup.Relationship, "self", "Self (own account)")
    ];

    public sealed record SeedSummary(int Corridors, int PayoutMethods, int Fields, int Institutions, int Lookups, int PaymentMethods);

    public static async Task<SeedSummary> SeedAsync(AppDbContext db, string paymentProvider, string payoutProvider, CancellationToken ct)
    {
        int corridors = 0, methods = 0, fields = 0, institutions = 0, lookups = 0, payments = 0;

        for (var r = 0; r < Routes.Length; r++)
        {
            var route = Routes[r];
            var corridor = await db.RemittanceCorridors.Include(c => c.PayoutMethods)
                .FirstOrDefaultAsync(c => c.SourceCountryCode == "MY" && c.DestinationCountryCode == route.Country && c.DestinationCurrency == route.Currency, ct);
            if (corridor is null)
            {
                corridor = new RemittanceCorridor
                {
                    SourceCountryCode = "MY",
                    SourceCurrency = "MYR",
                    DestinationCountryCode = route.Country,
                    DestinationCountryName = route.Name,
                    DestinationCurrency = route.Currency,
                    DestinationDecimals = route.Decimals,
                    MinSendAmount = 20m,
                    MaxSendAmount = 20_000m,
                    FxMarginBps = route.MarginBps,
                    SortOrder = r
                };
                db.RemittanceCorridors.Add(corridor);
                await db.SaveChangesAsync(ct);
                corridors++;
            }

            for (var m = 0; m < route.Methods.Length; m++)
            {
                var spec = route.Methods[m];
                if (corridor.PayoutMethods.Any(x => x.Code == spec.Code)) continue;

                var method = new RemittancePayoutMethod
                {
                    CorridorId = corridor.Id,
                    Code = spec.Code,
                    Name = spec.Name,
                    DeliveryEta = spec.Eta,
                    PayoutProvider = payoutProvider,
                    SortOrder = m,
                    FeeRule = new RemittanceFeeRule { FixedFee = spec.Fee, PercentFee = spec.Percent, FirstTransferFee = 0m, PromoLabel = "PROMO" }
                };
                for (var f = 0; f < spec.Fields.Length; f++)
                {
                    var fs = spec.Fields[f];
                    method.Fields.Add(new RemittanceBeneficiaryField
                    {
                        Key = fs.Key, Label = fs.Label, InputType = fs.Type, IsRequired = true, MinLength = fs.Min, MaxLength = fs.Max,
                        Pattern = fs.Pattern, HelperText = fs.Helper, IsAccountIdentifier = fs.Account, SortOrder = f
                    });
                    fields++;
                }
                for (var i = 0; i < spec.Institutions.Length; i++)
                {
                    method.Institutions.Add(new RemittanceInstitution { Code = spec.Institutions[i].Code, Name = spec.Institutions[i].Name, SortOrder = i });
                    institutions++;
                }
                db.RemittancePayoutMethods.Add(method);
                methods++;
            }
            await db.SaveChangesAsync(ct);
        }

        for (var i = 0; i < Lookups.Length; i++)
        {
            var (category, code, label) = Lookups[i];
            if (await db.RemittanceLookups.AnyAsync(l => l.Category == category && l.Code == code, ct)) continue;
            db.RemittanceLookups.Add(new RemittanceLookup { Category = category, Code = code, Label = label, SortOrder = i });
            lookups++;
        }

        if (!await db.RemittancePaymentMethods.AnyAsync(p => p.SourceCountryCode == "MY" && p.Code == "FPX", ct))
        {
            db.RemittancePaymentMethods.Add(new RemittancePaymentMethod
            {
                SourceCountryCode = "MY", Provider = paymentProvider, Code = "FPX", Label = "FPX Online Banking",
                Description = "Pay with Maybank2u, CIMB Clicks, Public Bank and more.", Badge = "INSTANT", PaymentWindowMinutes = 15, SortOrder = 0
            });
            payments++;
        }
        if (!await db.RemittancePaymentMethods.AnyAsync(p => p.SourceCountryCode == "MY" && p.Code == "COUNTER", ct))
        {
            db.RemittancePaymentMethods.Add(new RemittancePaymentMethod
            {
                SourceCountryCode = "MY", Provider = paymentProvider, Code = "COUNTER", Label = "Pay at Counter / Branch",
                Description = "Get a payment reference to pay at 7-Eleven or a branch.", Badge = "24H HOLD", PaymentWindowMinutes = 1440, SortOrder = 1
            });
            payments++;
        }

        await db.SaveChangesAsync(ct);
        return new SeedSummary(corridors, methods, fields, institutions, lookups, payments);
    }

    /// <summary>The "Send Money" bot flow: keyword trigger → WhatsApp Flow node. Saved through the same JSON the designer saves.</summary>
    public static string SendMoneyBotFlowData() => System.Text.Json.JsonSerializer.Serialize(new
    {
        nodes = new object[]
        {
            new
            {
                id = "trigger-1", type = "Start Trigger", position = new { x = 80, y = 120 },
                data = new { contactType = "Lead", triggerType = "on exact match", keywords = new[] { "send money", "/sendmoney", "sendmoney", "remit", "remittance" } }
            },
            new
            {
                id = "flow-1", type = "WhatsApp Flow", position = new { x = 420, y = 120 },
                data = new
                {
                    flowPurpose = WhatsAppFlowRegistration.SendMoneyPurpose,
                    headerText = "International Remittance",
                    bodyText = "Send money home with real-time FX rates and a guaranteed quote. Verify, choose your recipient and pay securely — all without leaving WhatsApp.",
                    footerText = "Powered by WhatsApp Flows",
                    ctaText = "Click here to send money"
                }
            }
        },
        edges = new object[] { new { id = "e-trigger-flow", source = "trigger-1", target = "flow-1", sourceHandle = (string?)null } }
    });
}
