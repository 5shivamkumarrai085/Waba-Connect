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

        RuleFor(x => x.TemplateId)
            .GreaterThan(0).WithMessage("TemplateId is required.");

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("RelationType is required.")
            .IsEnumName(typeof(ContactType), false).WithMessage("Invalid RelationType.");

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
