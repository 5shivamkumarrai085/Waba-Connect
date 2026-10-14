using System.ComponentModel.DataAnnotations;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Models.Entities;

// ── Send Money configuration ─────────────────────────────────────────────────────────────────
// Everything a customer can choose in the Send Money flow is a row here, edited under Setup ›
// Send Money. The WhatsApp Flow's screens are fixed (Meta cannot add components at runtime), so
// these rows decide what those screens offer: which countries, which payout methods, which
// beneficiary fields, what it costs. Lengths follow Meta's Flow component limits — a label longer
// than the Flow can render is refused when it is saved, not discovered on a customer's phone.

/// <summary>One "send from → send to" route, e.g. Malaysia (MYR) → Indonesia (IDR).</summary>
public class RemittanceCorridor
{
    public int Id { get; set; }

    /// <summary>ISO 3166-1 alpha-2 of the sending country, upper case.</summary>
    [Required, MaxLength(2)]
    public string SourceCountryCode { get; set; } = string.Empty;

    /// <summary>ISO 4217 code the customer pays in.</summary>
    [Required, MaxLength(3)]
    public string SourceCurrency { get; set; } = string.Empty;

    [Required, MaxLength(2)]
    public string DestinationCountryCode { get; set; } = string.Empty;

    /// <summary>Shown in the country picker (a Flow dropdown title holds 30 characters).</summary>
    [Required, MaxLength(30)]
    public string DestinationCountryName { get; set; } = string.Empty;

    [Required, MaxLength(3)]
    public string DestinationCurrency { get; set; } = string.Empty;

    /// <summary>Minor units of the receive currency: 2 for PHP, 0 for IDR and VND.</summary>
    public int DestinationDecimals { get; set; } = 2;

    public decimal MinSendAmount { get; set; }
    public decimal MaxSendAmount { get; set; }

    /// <summary>The business's FX margin on the mid-market rate, in basis points (100 = 1%).</summary>
    public int FxMarginBps { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RemittancePayoutMethod> PayoutMethods { get; set; } = new();
}

/// <summary>How the money reaches the recipient on one corridor: bank transfer, e-wallet, cash pickup…</summary>
public class RemittancePayoutMethod
{
    public int Id { get; set; }

    public int CorridorId { get; set; }
    public RemittanceCorridor Corridor { get; set; } = null!;

    /// <summary>Stable key, e.g. BANK_TRANSFER.</summary>
    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Name { get; set; } = string.Empty;

    /// <summary>e.g. "Instant / within 15 mins".</summary>
    [MaxLength(60)]
    public string? DeliveryEta { get; set; }

    /// <summary>Which payout adapter delivers it (IPayoutProvider.ProviderName).</summary>
    [Required, MaxLength(40)]
    public string PayoutProvider { get; set; } = "Mock";

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RemittanceBeneficiaryField> Fields { get; set; } = new();
    public List<RemittanceInstitution> Institutions { get; set; } = new();
    public RemittanceFeeRule? FeeRule { get; set; }
}

/// <summary>
/// One piece of recipient detail a payout method needs (account number, IFSC, e-wallet id…).
/// The Flow has a fixed number of text slots; each active field fills one, in sort order.
/// </summary>
public class RemittanceBeneficiaryField
{
    /// <summary>How many fields the Send Money Flow can show for one payout method.</summary>
    public const int MaxSlots = 6;

    public int Id { get; set; }

    public int PayoutMethodId { get; set; }
    public RemittancePayoutMethod PayoutMethod { get; set; } = null!;

    /// <summary>Stable key stored with the beneficiary, e.g. account_number.</summary>
    [Required, MaxLength(40)]
    public string Key { get; set; } = string.Empty;

    /// <summary>Text input label — Meta caps it at 20 characters.</summary>
    [Required, MaxLength(20)]
    public string Label { get; set; } = string.Empty;

    public RemittanceFieldInputType InputType { get; set; } = RemittanceFieldInputType.Text;

    public bool IsRequired { get; set; } = true;
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }

    /// <summary>Optional .NET regular expression the whole value must match.</summary>
    [MaxLength(200)]
    public string? Pattern { get; set; }

    /// <summary>Help under the input — Meta caps it at 80 characters.</summary>
    [MaxLength(80)]
    public string? HelperText { get; set; }

    /// <summary>Masked and shown on the summary (the account number, typically).</summary>
    public bool IsAccountIdentifier { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A bank or wallet a payout method can deliver to.</summary>
public class RemittanceInstitution
{
    public int Id { get; set; }

    public int PayoutMethodId { get; set; }
    public RemittancePayoutMethod PayoutMethod { get; set; } = null!;

    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Dropdown title — Meta caps it at 30 characters.</summary>
    [Required, MaxLength(30)]
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>What one payout method costs: fixed + percentage, clamped, with an optional first-transfer price.</summary>
public class RemittanceFeeRule
{
    public int Id { get; set; }

    public int PayoutMethodId { get; set; }
    public RemittancePayoutMethod PayoutMethod { get; set; } = null!;

    public decimal FixedFee { get; set; }

    /// <summary>Percentage of the send amount, e.g. 0.5 for 0.5%.</summary>
    public decimal PercentFee { get; set; }

    public decimal? MinFee { get; set; }
    public decimal? MaxFee { get; set; }

    /// <summary>Replaces the computed fee on a customer's first completed transfer.</summary>
    public decimal? FirstTransferFee { get; set; }

    /// <summary>Badge shown next to the fee when a promotion applies, e.g. PROMO.</summary>
    [MaxLength(20)]
    public string? PromoLabel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Pick-list values the regulator asks for: purpose, source of funds, relationship.</summary>
public class RemittanceLookup
{
    public const string Purpose = "purpose";
    public const string SourceOfFunds = "source_of_funds";
    public const string Relationship = "relationship";
    public static readonly string[] Categories = [Purpose, SourceOfFunds, Relationship];

    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Category { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Dropdown title — Meta caps it at 30 characters.</summary>
    [Required, MaxLength(30)]
    public string Label { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A way to pay offered to customers in one sending country (FPX online banking, pay at counter…).</summary>
public class RemittancePaymentMethod
{
    public int Id { get; set; }

    [Required, MaxLength(2)]
    public string SourceCountryCode { get; set; } = string.Empty;

    /// <summary>Which payment adapter takes the money (IPaymentProvider.ProviderName).</summary>
    [Required, MaxLength(40)]
    public string Provider { get; set; } = "Mock";

    /// <summary>Method key passed to the provider, e.g. FPX or COUNTER.</summary>
    [Required, MaxLength(40)]
    public string Code { get; set; } = string.Empty;

    /// <summary>Radio button title — Meta caps it at 30 characters.</summary>
    [Required, MaxLength(30)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>Short tag beside the option, e.g. INSTANT or 24H HOLD (Meta caps metadata at 20).</summary>
    [MaxLength(20)]
    public string? Badge { get; set; }

    /// <summary>How long the customer has to pay before the order expires.</summary>
    public int PaymentWindowMinutes { get; set; } = 15;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ── Send Money runtime ───────────────────────────────────────────────────────────────────────

/// <summary>
/// One customer's pass through the Send Money Flow, keyed by the Flow's <c>flow_token</c>.
/// </summary>
/// <remarks>
/// The server decides every step from this row, never from what the Flow sends back: a screen
/// that claims the customer is verified, or skips ahead to "confirm", is checked against
/// <see cref="OtpVerifiedAt"/> and the stored selections. Only a SHA-256 of the token is kept, so
/// reading this table does not let anyone drive somebody else's session.
/// </remarks>
public class RemittanceSession
{
    public Guid Id { get; set; }

    [Required, MaxLength(64)]
    public string FlowTokenHash { get; set; } = string.Empty;

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    /// <summary>The WhatsApp number the Flow was sent to — the identity being verified.</summary>
    [Required, MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;

    public int? ConnectionId { get; set; }

    public RemittanceSessionStatus Status { get; set; } = RemittanceSessionStatus.Started;
    public DateTime ExpiresAt { get; set; }
    public DateTime? OtpVerifiedAt { get; set; }

    public int? CorridorId { get; set; }
    public int? PayoutMethodId { get; set; }
    public decimal? SendAmount { get; set; }
    public Guid? QuoteId { get; set; }
    public int? BeneficiaryId { get; set; }

    [MaxLength(40)]
    public string? SourceOfFundsCode { get; set; }

    [MaxLength(40)]
    public string? PurposeCode { get; set; }

    [MaxLength(140)]
    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A one-time code issued for one purpose and subject. Only a keyed hash of the code is stored.
/// </summary>
public class OtpChallenge
{
    public long Id { get; set; }

    [Required, MaxLength(40)]
    public string Purpose { get; set; } = string.Empty;

    /// <summary>What the code unlocks — for Send Money, the session id.</summary>
    [Required, MaxLength(64)]
    public string SubjectKey { get; set; } = string.Empty;

    /// <summary>Where it was delivered (a phone number).</summary>
    [Required, MaxLength(32)]
    public string Destination { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>The IOtpProvider that delivered it.</summary>
    [Required, MaxLength(40)]
    public string Provider { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; }
    public int SendCount { get; set; } = 1;
    public DateTime LastSentAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? LockedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A price the customer was shown: the rate, fee and amounts, locked until <see cref="ExpiresAt"/>.
/// The order is built from the quote, never from amounts the Flow sends back.
/// </summary>
public class FxQuote
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }
    public int CorridorId { get; set; }
    public int PayoutMethodId { get; set; }

    [Required, MaxLength(3)]
    public string SendCurrency { get; set; } = string.Empty;
    public decimal SendAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal TotalPayable { get; set; }

    [MaxLength(20)]
    public string? FeeLabel { get; set; }

    [Required, MaxLength(3)]
    public string ReceiveCurrency { get; set; } = string.Empty;
    public decimal ReceiveAmount { get; set; }

    /// <summary>Mid-market rate from the provider: 1 SendCurrency = MidRate ReceiveCurrency.</summary>
    public decimal MidRate { get; set; }

    /// <summary>The rate the customer gets, after the corridor's margin.</summary>
    public decimal AppliedRate { get; set; }
    public int MarginBps { get; set; }

    [Required, MaxLength(40)]
    public string RateProvider { get; set; } = string.Empty;

    /// <summary>When the provider says the rate was published — not when we fetched it.</summary>
    public DateTime RateAsOf { get; set; }

    public FxQuoteStatus Status { get; set; } = FxQuoteStatus.Active;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A saved recipient. The account details are encrypted at rest.</summary>
public class RemittanceBeneficiary
{
    public int Id { get; set; }

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public int CorridorId { get; set; }
    public int PayoutMethodId { get; set; }
    public int? InstitutionId { get; set; }

    [Required, MaxLength(80)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>e.g. ****8234 — safe to show anywhere.</summary>
    [MaxLength(24)]
    public string? MaskedAccount { get; set; }

    [MaxLength(40)]
    public string? RelationshipCode { get; set; }

    /// <summary>Field key → value as JSON, encrypted by the column converter.</summary>
    [Required]
    public string DetailsJson { get; set; } = "{}";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A transfer the customer confirmed. Order status and payment status move independently; only a
/// verified provider callback can mark the payment as succeeded.
/// </summary>
public class TransferOrder
{
    public int Id { get; set; }

    /// <summary>Customer-facing reference, e.g. IR-MY559356.</summary>
    [Required, MaxLength(20)]
    public string Reference { get; set; } = string.Empty;

    /// <summary>One order per session — confirming twice returns the same order.</summary>
    public Guid SessionId { get; set; }
    public Guid QuoteId { get; set; }

    public int ContactId { get; set; }
    public Contact Contact { get; set; } = null!;

    public int? ConnectionId { get; set; }
    public int CorridorId { get; set; }
    public int PayoutMethodId { get; set; }
    public int BeneficiaryId { get; set; }

    // Snapshot at confirmation, so editing configuration later never rewrites a past transfer.
    [Required, MaxLength(80)]
    public string BeneficiaryName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? InstitutionName { get; set; }

    [MaxLength(24)]
    public string? MaskedAccount { get; set; }

    [Required, MaxLength(30)]
    public string PayoutMethodName { get; set; } = string.Empty;

    [Required, MaxLength(3)]
    public string SendCurrency { get; set; } = string.Empty;
    public decimal SendAmount { get; set; }
    public decimal FeeAmount { get; set; }
    public decimal TotalPayable { get; set; }

    [Required, MaxLength(3)]
    public string ReceiveCurrency { get; set; } = string.Empty;
    public decimal ReceiveAmount { get; set; }
    public decimal AppliedRate { get; set; }

    [MaxLength(40)]
    public string? SourceOfFundsCode { get; set; }

    [MaxLength(40)]
    public string? PurposeCode { get; set; }

    [MaxLength(140)]
    public string? Remarks { get; set; }

    [Required, MaxLength(40)]
    public string PaymentMethodCode { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string PaymentProvider { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? ProviderPaymentId { get; set; }

    [MaxLength(500)]
    public string? PaymentUrl { get; set; }

    /// <summary>Counter/biller reference when the method is paid in person.</summary>
    [MaxLength(60)]
    public string? PaymentReference { get; set; }

    public DateTime? PaymentExpiresAt { get; set; }

    public TransferOrderStatus OrderStatus { get; set; } = TransferOrderStatus.Created;
    public TransferPaymentStatus PaymentStatus { get; set; } = TransferPaymentStatus.Pending;

    [Required, MaxLength(40)]
    public string PayoutProvider { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? PayoutReference { get; set; }

    public DateTime? PaidAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Set once the "order created / pay here" message has gone out, so it is sent once.</summary>
    public DateTime? CustomerNotifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only history of a transfer: every status change and every provider callback.</summary>
public class TransferOrderEvent
{
    public long Id { get; set; }

    public int TransferOrderId { get; set; }

    /// <summary>OrderStatus, PaymentStatus, PaymentCallback, PayoutCallback, Notification.</summary>
    [Required, MaxLength(40)]
    public string Kind { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? FromStatus { get; set; }

    [MaxLength(40)]
    public string? ToStatus { get; set; }

    /// <summary>System, Customer, ProviderWebhook or Admin.</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = "System";

    [MaxLength(40)]
    public string? Provider { get; set; }

    /// <summary>The provider's own event id; unique per provider, which makes callbacks idempotent.</summary>
    [MaxLength(100)]
    public string? ProviderEventId { get; set; }

    [MaxLength(500)]
    public string? Detail { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
