using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.Entities;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Services.Remittance;

/// <summary>What the customer typed for a new recipient. Field values are keyed by slot order.</summary>
public sealed record NewBeneficiaryInput(string? FullName, string? InstitutionCode, string? RelationshipCode, IReadOnlyList<string?> SlotValues);

public sealed record BeneficiaryResult(RemittanceBeneficiary? Beneficiary, string? Error)
{
    public bool Ok => Beneficiary is not null;
}

public interface IBeneficiaryService
{
    /// <summary>Saved, active recipients of this customer for one corridor and payout method.</summary>
    Task<List<RemittanceBeneficiary>> ListAsync(int contactId, int corridorId, int payoutMethodId, CancellationToken ct);

    /// <summary>Validates against the payout method's configured fields and saves the recipient encrypted.</summary>
    Task<BeneficiaryResult> CreateAsync(int contactId, int corridorId, int payoutMethodId, NewBeneficiaryInput input, CancellationToken ct);

    /// <summary>Stored field values (decrypted), keyed by field key.</summary>
    Dictionary<string, string> ReadDetails(RemittanceBeneficiary beneficiary);
}

public sealed class BeneficiaryService : IBeneficiaryService
{
    /// <summary>Most saved recipients offered at once — a Flow radio group holds 20 options, one is "Add new".</summary>
    public const int MaxListed = 19;

    private readonly AppDbContext _db;

    public BeneficiaryService(AppDbContext db) => _db = db;

    public Task<List<RemittanceBeneficiary>> ListAsync(int contactId, int corridorId, int payoutMethodId, CancellationToken ct) =>
        _db.RemittanceBeneficiaries.AsNoTracking()
            .Where(b => b.ContactId == contactId && b.CorridorId == corridorId && b.PayoutMethodId == payoutMethodId && b.IsActive)
            .OrderByDescending(b => b.UpdatedAt)
            .Take(MaxListed)
            .ToListAsync(ct);

    public async Task<BeneficiaryResult> CreateAsync(int contactId, int corridorId, int payoutMethodId, NewBeneficiaryInput input, CancellationToken ct)
    {
        var method = await _db.RemittancePayoutMethods.AsNoTracking()
            .Include(m => m.Fields.Where(f => f.IsActive))
            .Include(m => m.Institutions.Where(i => i.IsActive))
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == payoutMethodId && m.CorridorId == corridorId && m.IsActive, ct);
        if (method is null) return new(null, "That delivery method is no longer available.");

        var name = (input.FullName ?? string.Empty).Trim();
        if (name.Length < 2) return new(null, "Enter the recipient's full name.");
        if (name.Length > 80) return new(null, "The recipient's name is too long.");

        RemittanceInstitution? institution = null;
        if (method.Institutions.Count > 0)
        {
            institution = method.Institutions.FirstOrDefault(i => string.Equals(i.Code, input.InstitutionCode, StringComparison.Ordinal));
            if (institution is null) return new(null, "Choose the recipient's bank or provider.");
        }

        // Relationship is asked only when the business has configured the pick-list.
        var relationships = await _db.RemittanceLookups.AsNoTracking()
            .Where(l => l.Category == RemittanceLookup.Relationship && l.IsActive)
            .Select(l => l.Code).ToListAsync(ct);
        if (relationships.Count > 0 && !relationships.Contains(input.RelationshipCode ?? string.Empty))
        {
            return new(null, "Choose how you know the recipient.");
        }
        var relationshipCode = relationships.Count > 0 ? input.RelationshipCode : null;

        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        string? accountValue = null;
        var fields = OrderedSlots(method.Fields);

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var value = (i < input.SlotValues.Count ? input.SlotValues[i] : null)?.Trim() ?? string.Empty;
            var problem = Validate(field, value);
            if (problem is not null) return new(null, problem);
            if (value.Length > 0) details[field.Key] = value;
            if (field.IsAccountIdentifier && value.Length > 0) accountValue = value;
        }

        var beneficiary = new RemittanceBeneficiary
        {
            ContactId = contactId,
            CorridorId = corridorId,
            PayoutMethodId = payoutMethodId,
            InstitutionId = institution?.Id,
            DisplayName = name,
            MaskedAccount = Mask(accountValue),
            RelationshipCode = relationshipCode,
            DetailsJson = JsonSerializer.Serialize(details)
        };

        _db.RemittanceBeneficiaries.Add(beneficiary);
        await _db.SaveChangesAsync(ct);
        return new(beneficiary, null);
    }

    public Dictionary<string, string> ReadDetails(RemittanceBeneficiary beneficiary)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(beneficiary.DetailsJson) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    /// <summary>The fields a payout method shows, in slot order, capped at the Flow's slot count.</summary>
    public static List<RemittanceBeneficiaryField> OrderedSlots(IEnumerable<RemittanceBeneficiaryField> fields) =>
        fields.Where(f => f.IsActive).OrderBy(f => f.SortOrder).ThenBy(f => f.Id).Take(RemittanceBeneficiaryField.MaxSlots).ToList();

    /// <summary>Null when the value is acceptable for the field; otherwise what to tell the customer.</summary>
    public static string? Validate(RemittanceBeneficiaryField field, string value)
    {
        if (value.Length == 0) return field.IsRequired ? $"{field.Label} is required." : null;
        if (field.MinLength is { } min && value.Length < min) return $"{field.Label} must be at least {min} characters.";
        if (field.MaxLength is { } max && value.Length > max) return $"{field.Label} can be at most {max} characters.";

        switch (field.InputType)
        {
            case RemittanceFieldInputType.Number when !value.All(char.IsAsciiDigit):
                return $"{field.Label} must contain digits only.";
            case RemittanceFieldInputType.Phone when !Regex.IsMatch(value, @"^\+?[0-9 ]{6,20}$"):
                return $"{field.Label} must be a phone number.";
            case RemittanceFieldInputType.Email when !MailAddress.TryCreate(value, out _):
                return $"{field.Label} must be an email address.";
        }

        if (!string.IsNullOrWhiteSpace(field.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(value, $"^(?:{field.Pattern})$", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
                    return $"{field.Label} is not in the expected format.";
            }
            catch (RegexMatchTimeoutException)
            {
                return $"{field.Label} could not be checked.";
            }
        }

        return null;
    }

    /// <summary>Keeps the last four characters: 8204918234 → ****8234.</summary>
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Replace(" ", string.Empty);
        return v.Length <= 4 ? new string('*', v.Length) : "****" + v[^4..];
    }
}
