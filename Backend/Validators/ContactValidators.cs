using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Services.Catalogs;

namespace WhatsAppCampaignApi.Validators;

/// <summary>
/// Looks up what contact validation needs: the valid types, statuses and sources (all
/// administrator-managed lookups) and which fields are required (OmniConnect Settings ›
/// Contacts, <see cref="ContactFieldCatalog.RequiredFieldsSettingKey"/>).
///
/// <para>
/// Deliberately synchronous. This app registers FluentValidation through
/// <c>AddFluentValidationAutoValidation()</c>, and that pipeline is synchronous — a validator
/// containing any <c>MustAsync</c> rule throws at request time rather than validating, which
/// takes down contact create and update entirely.
/// </para>
/// <para>
/// The synchronous database hit is made cheap by caching: the lists are small, rarely change, and
/// are shared across all requests for 60 seconds. Saving the Contacts settings evicts the
/// required-fields entry at once (see <c>OmniSettingsService.InvalidateCache</c>).
/// </para>
/// </summary>
public class ContactLookupValidatorCache
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string StatusCacheKey = "validator:contact-statuses";
    private const string SourceCacheKey = "validator:contact-sources";
    private const string TypeCacheKey = "validator:contact-types";
    public const string RequiredFieldsCacheKey = "validator:contact-required-fields";

    public ContactLookupValidatorCache(AppDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public bool StatusExists(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GetStatuses().Contains(value);

    public bool SourceExists(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GetSources().Contains(value);

    public bool TypeExists(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GetTypes().Contains(value);

    /// <summary>The effective set of required field keys.</summary>
    public HashSet<string> RequiredFields() =>
        _cache.GetOrCreate(RequiredFieldsCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var raw = _dbContext.AppSettings.AsNoTracking()
                .Where(s => s.Key == ContactFieldCatalog.RequiredFieldsSettingKey)
                .Select(s => s.Value)
                .FirstOrDefault();
            return ContactFieldCatalog.Resolve(ContactFieldCatalog.ParseStored(raw));
        })!;

    public bool IsRequired(string key) => RequiredFields().Contains(key);

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

    private HashSet<string> GetTypes() =>
        _cache.GetOrCreate(TypeCacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return _dbContext.ContactTypes
                .AsNoTracking()
                .Select(s => s.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        })!;
}

/// <summary>
/// One rule set for create and update. Every limit and requirement comes from
/// <see cref="ContactFieldCatalog"/> and the Contacts settings, and type, status and source are
/// checked against the lookups administrators manage, so adding a type or making a field
/// mandatory never needs a code change.
/// </summary>
public abstract class ContactRequestValidatorBase<T> : AbstractValidator<T> where T : CreateContactRequest
{
    protected ContactRequestValidatorBase(ContactLookupValidatorCache lookups)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .Length(ContactFieldCatalog.NameMinLength, Max("name"))
            .WithMessage($"Name must be between {ContactFieldCatalog.NameMinLength} and {Max("name")} characters.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone is required.")
            .Matches(ContactFieldCatalog.PhonePattern).WithMessage("Phone must be in international format, e.g. +919499373415.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Type is required.")
            .Must(lookups.TypeExists).WithMessage("Choose a contact type from the list.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("Status is required.")
            .Must(lookups.StatusExists).WithMessage("Choose a status from the list.");

        RuleFor(x => x.Source)
            .NotEmpty().WithMessage("Source is required.")
            .Must(lookups.SourceExists).WithMessage("Choose a source from the list.");

        Text(x => x.Email, "email", lookups);
        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Enter a valid email address, e.g. name@example.com.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        Text(x => x.Company, "company", lookups);
        Text(x => x.Website, "website", lookups);
        RuleFor(x => x.Website)
            .Must(v => Uri.TryCreate(v!.Contains("://") ? v : "https://" + v, UriKind.Absolute, out _))
            .WithMessage("Enter a valid website address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Website));
        Text(x => x.AssignedTo, "assignedTo", lookups);
        Text(x => x.City, "city", lookups);
        Text(x => x.State, "state", lookups);
        Text(x => x.Country, "country", lookups);
        Text(x => x.TimeZone, "timeZone", lookups);
        Text(x => x.ZipCode, "zipCode", lookups);
        Text(x => x.Address, "address", lookups);
        Text(x => x.Description, "description", lookups);

        var ages = ContactFieldCatalog.AgeYears;
        RuleFor(x => x.DateOfBirth)
            .NotNull().WithMessage("Date of birth is required.")
            .When(_ => lookups.IsRequired("dateOfBirth"));
        RuleFor(x => x.DateOfBirth)
            .Must(dob => dob!.Value <= DateOnly.FromDateTime(DateTime.UtcNow)).WithMessage("Date of birth cannot be in the future.")
            .Must(dob => ContactFieldCatalog.AgeOn(dob!.Value, DateOnly.FromDateTime(DateTime.UtcNow)) is var age && age >= ages.Min && age <= ages.Max)
            .WithMessage($"Age must be between {ages.Min} and {ages.Max} years.")
            .When(x => x.DateOfBirth.HasValue);

        RuleForEach(x => x.GroupIds)
            .GreaterThan(0).WithMessage("Group IDs must be greater than 0.")
            .When(x => x.GroupIds != null);
    }

    private static int Max(string key) => ContactFieldCatalog.Find(key)?.MaxLength ?? int.MaxValue;

    /// <summary>A free-text field: its length from the catalog, required only when the Contacts settings say so.</summary>
    private void Text(System.Linq.Expressions.Expression<Func<T, string?>> field, string key, ContactLookupValidatorCache lookups)
    {
        var label = ContactFieldCatalog.Find(key)?.Label ?? key;
        var max = Max(key);
        RuleFor(field)
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage($"{label} is required.")
            .When(_ => lookups.IsRequired(key));
        RuleFor(field)
            .MaximumLength(max).WithMessage($"{label} cannot be longer than {max} characters.");
    }
}

public class CreateContactValidator : ContactRequestValidatorBase<CreateContactRequest>
{
    public CreateContactValidator(ContactLookupValidatorCache lookups) : base(lookups) { }
}

public class UpdateContactValidator : ContactRequestValidatorBase<UpdateContactRequest>
{
    public UpdateContactValidator(ContactLookupValidatorCache lookups) : base(lookups) { }
}
