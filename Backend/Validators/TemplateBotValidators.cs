using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.TemplateBot;

namespace WhatsAppCampaignApi.Validators;

public class CreateTemplateBotRequestValidator : AbstractValidator<CreateTemplateBotRequest>
{
    public CreateTemplateBotRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Name is required.")
            .MaximumLength(100).WithMessage("Bot Name cannot exceed 100 characters.");

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("Relation Type is required.")
            .Must(x => x == "Lead" || x == "Customer").WithMessage("Relation Type must be 'Lead' or 'Customer'.");

        RuleFor(x => x.TemplateId)
            .GreaterThan(0).WithMessage("Template selection is required.");

        RuleFor(x => x.ReplyType)
            .NotEmpty().WithMessage("Reply Type is required.");

        RuleFor(x => x.TriggerKeyword)
            .NotEmpty().WithMessage("Trigger Keyword is required.")
            .MaximumLength(256).WithMessage("Trigger Keyword cannot exceed 256 characters.");
    }
}

public class UpdateTemplateBotRequestValidator : AbstractValidator<UpdateTemplateBotRequest>
{
    public UpdateTemplateBotRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Name is required.")
            .MaximumLength(100).WithMessage("Bot Name cannot exceed 100 characters.");

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("Relation Type is required.")
            .Must(x => x == "Lead" || x == "Customer").WithMessage("Relation Type must be 'Lead' or 'Customer'.");

        RuleFor(x => x.TemplateId)
            .GreaterThan(0).WithMessage("Template selection is required.");

        RuleFor(x => x.ReplyType)
            .NotEmpty().WithMessage("Reply Type is required.");

        RuleFor(x => x.TriggerKeyword)
            .NotEmpty().WithMessage("Trigger Keyword is required.")
            .MaximumLength(256).WithMessage("Trigger Keyword cannot exceed 256 characters.");
    }
}
