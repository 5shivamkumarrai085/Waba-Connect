using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Validators;

public class CreateCampaignValidator : AbstractValidator<CreateCampaignRequest>
{
    public CreateCampaignValidator(ContactLookupValidatorCache lookups)
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .Length(2, 200).WithMessage("Name must be between 2 and 200 characters.");

        // Per channel. TemplateId is the WhatsApp template and an email campaign has none —
        // requiring it unconditionally rejected every email campaign at the final step, long
        // after the wizard had stopped offering a WhatsApp template to choose.
        RuleFor(x => x.Channel)
            .Must(BeAKnownChannel)
            .WithMessage($"Channel must be one of: {string.Join(", ", Enum.GetNames<MessageChannel>())}.");

        RuleFor(x => x.TemplateId)
            .GreaterThan(0).WithMessage("TemplateId is required.")
            .When(x => !IsEmail(x.Channel));

        RuleFor(x => x.EmailTemplateId)
            .NotNull().WithMessage("An email template is required for an email campaign.")
            .GreaterThan(0).WithMessage("An email template is required for an email campaign.")
            .When(x => IsEmail(x.Channel));

        RuleFor(x => x.SenderIdentityId)
            .NotNull().WithMessage("A sender email is required for an email campaign.")
            .GreaterThan(0).WithMessage("A sender email is required for an email campaign.")
            .When(x => IsEmail(x.Channel));

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("Choose at least one contact type.")
            .Must(value => BeValidRelationTypeList(value, lookups))
            .WithMessage("Each contact type must be one of the types set up under Setup › Contact types.");

        RuleFor(x => x.ScheduleType)
            .NotEmpty().WithMessage("ScheduleType is required.")
            .IsEnumName(typeof(ScheduleType), false).WithMessage("Invalid ScheduleType.");

        RuleFor(x => x.ScheduledAt)
            .NotEmpty().When(x => x.ScheduleType.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
            .WithMessage("ScheduledAt is required when ScheduleType is Scheduled.")
            .GreaterThan(DateTime.UtcNow).When(x => x.ScheduledAt.HasValue
                && x.ScheduleType.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
            .WithMessage("ScheduledAt must be in the future.");

        // A wall-clock time: compared generously (a day either side of UTC covers every zone).
        RuleFor(x => x.LocalSendAt)
            .NotEmpty().When(x => x.ScheduleType.Equals("RecipientLocalTime", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Choose the local date and time to deliver at.")
            .Must(t => t is null || t.Value > DateTime.UtcNow.AddHours(-14))
            .WithMessage("The local send time is in the past for every time zone.");

        RuleFor(x => x.Topic)
            .MaximumLength(64).WithMessage("The consent topic can be at most 64 characters.");

        // "Select all" and segments are audiences too — the old rule rejected both.
        RuleFor(x => x)
            .Must(x => x.SelectAllContacts
                || (x.ContactIds != null && x.ContactIds.Any())
                || (x.GroupIds != null && x.GroupIds.Any())
                || (x.SegmentIds != null && x.SegmentIds.Any()))
            .WithMessage("Choose who receives the campaign: contacts, groups, segments or all contacts.");

        RuleForEach(x => x.Variables).SetValidator(new CampaignVariableValidator());
    }

    /// <summary>
    /// Treats anything that is not a recognised channel as WhatsApp, matching the service's own
    /// ParseChannel: an absent or empty Channel means a request written before the email channel
    /// existed, and those are WhatsApp campaigns. An unrecognised non-empty value is caught by
    /// the Channel rule itself, so this does not have to reject it twice.
    /// </summary>
    private static bool IsEmail(string? channel) =>
        string.Equals(channel?.Trim(), nameof(MessageChannel.Email), StringComparison.OrdinalIgnoreCase);

    private static bool BeAKnownChannel(string? channel) =>
        string.IsNullOrWhiteSpace(channel) || Enum.TryParse<MessageChannel>(channel, true, out _);

    // A campaign can target several contact types at once — RelationType arrives as a
    // comma-separated string (e.g. "Lead,Customer"); every token must be a type from the
    // administrator-managed ContactTypes lookup.
    private static bool BeValidRelationTypeList(string value, ContactLookupValidatorCache lookups)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(lookups.TypeExists);
    }
}

public class CampaignVariableValidator : AbstractValidator<CampaignVariableRequest>
{
    public CampaignVariableValidator()
    {
        RuleFor(x => x.VariableName)
            .NotEmpty().WithMessage("VariableName is required.")
            .MaximumLength(50).WithMessage("VariableName cannot exceed 50 characters.");
    }
}
