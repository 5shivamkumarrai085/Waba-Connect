using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Campaigns;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Validators;

public class CreateCampaignValidator : AbstractValidator<CreateCampaignRequest>
{
    public CreateCampaignValidator()
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
            .NotEmpty().WithMessage("RelationType is required.")
            .Must(BeValidRelationTypeList)
            .WithMessage("Invalid RelationType. Must be a comma-separated list of: Lead, Customer, Vendor.");

        RuleFor(x => x.ScheduleType)
            .NotEmpty().WithMessage("ScheduleType is required.")
            .IsEnumName(typeof(ScheduleType), false).WithMessage("Invalid ScheduleType.");

        RuleFor(x => x.ScheduledAt)
            .NotEmpty().When(x => x.ScheduleType.Equals("Scheduled", StringComparison.OrdinalIgnoreCase))
            .WithMessage("ScheduledAt is required when ScheduleType is Scheduled.")
            .GreaterThan(DateTime.UtcNow).When(x => x.ScheduledAt.HasValue)
            .WithMessage("ScheduledAt must be in the future.");

        RuleFor(x => x)
            .Must(x => (x.ContactIds != null && x.ContactIds.Any()) || (x.GroupIds != null && x.GroupIds.Any()))
            .WithMessage("At least one Contact or Group must be selected.");

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

    // A campaign can now target multiple relation types at once — RelationType arrives
    // as a comma-separated string (e.g. "Lead,Customer"); every token must be a valid
    // ContactType name.
    private static bool BeValidRelationTypeList(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var tokens = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(t => Enum.TryParse<ContactType>(t, true, out _));
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
