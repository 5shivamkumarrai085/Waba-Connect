using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Validators;

/// <summary>
/// Looks up valid contact status and source values for validation.
///
/// <para>
/// Deliberately synchronous. This app registers FluentValidation through
/// <c>AddFluentValidationAutoValidation()</c>, and that pipeline is synchronous — a validator
/// containing any <c>MustAsync</c> rule throws at request time rather than validating, which
/// takes down contact create and update entirely.
/// </para>
/// <para>
/// The synchronous database hit is made cheap by caching: both lookups are small, rarely
/// change, and are shared across all requests for 60 seconds. Saving a status or source in the
/// Setup UI does not evict this, so a newly added value can take up to a minute to become
/// selectable — an acceptable trade for not blocking every contact save on two queries.
/// </para>
/// </summary>
public class ContactLookupValidatorCache
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string StatusCacheKey = "validator:contact-statuses";
    private const string SourceCacheKey = "validator:contact-sources";

    public ContactLookupValidatorCache(AppDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public bool StatusExists(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GetStatuses().Contains(value);

    public bool SourceExists(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GetSources().Contains(value);

    private HashSet<string> GetStatuses() =>
        _cache.GetOrCreate(StatusCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return _dbContext.ContactStatuses
                .AsNoTracking()
                .Select(s => s.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        })!;

    private HashSet<string> GetSources() =>
        _cache.GetOrCreate(SourceCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return _dbContext.ContactSources
                .AsNoTracking()
                .Select(s => s.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        })!;
}

/// <remarks>
/// Status and Source validate against the database rather than an enum, now that administrators
/// can define their own. Type stays an enum check — contact type has no lookup table and isn't
/// part of the Setup module.
/// </remarks>
public class CreateContactValidator : AbstractValidator<CreateContactRequest>
{
    public CreateContactValidator(ContactLookupValidatorCache lookups)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .Length(2, 100).WithMessage("Name must be between 2 and 100 characters.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Matches(@"^\+[1-9]\d{6,14}$").WithMessage("Phone must be in valid E.164 format (e.g. +919499373415).");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Type is required.")
            .IsEnumName(typeof(ContactType), caseSensitive: false).WithMessage("Invalid Contact Type.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(lookups.StatusExists).WithMessage("Invalid Contact Status.");

        RuleFor(x => x.Source)
            .NotEmpty().WithMessage("Source is required.")
            .Must(lookups.SourceExists).WithMessage("Invalid Contact Source.");

        RuleFor(x => x.AssignedTo)
            .MaximumLength(100).WithMessage("AssignedTo cannot exceed 100 characters.");

        RuleForEach(x => x.GroupIds)
            .GreaterThan(0).WithMessage("Group IDs must be greater than 0.")
            .When(x => x.GroupIds != null);
    }
}

public class UpdateContactValidator : AbstractValidator<UpdateContactRequest>
{
    public UpdateContactValidator(ContactLookupValidatorCache lookups)
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{6,14}$");
        RuleFor(x => x.Type).NotEmpty().IsEnumName(typeof(ContactType), false);

        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(lookups.StatusExists).WithMessage("Invalid Contact Status.");

        RuleFor(x => x.Source)
            .NotEmpty()
            .Must(lookups.SourceExists).WithMessage("Invalid Contact Source.");

        RuleFor(x => x.AssignedTo).MaximumLength(100);
        RuleForEach(x => x.GroupIds).GreaterThan(0).When(x => x.GroupIds != null);
    }
}
