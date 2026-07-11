using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.MessageBot;

namespace WhatsAppCampaignApi.Validators;

public class CreateMessageBotRequestValidator : AbstractValidator<CreateMessageBotRequest>
{
    public CreateMessageBotRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Name is required.")
            .MaximumLength(100).WithMessage("Bot Name cannot exceed 100 characters.");

        RuleFor(x => x.TriggerKeyword)
            .NotEmpty().WithMessage("Trigger Keyword is required.")
            .MaximumLength(256).WithMessage("Trigger Keyword cannot exceed 256 characters.");

        RuleFor(x => x.ReplyText)
            .NotEmpty().WithMessage("Reply Text is required.")
            .MaximumLength(1024).WithMessage("Reply Text cannot exceed 1024 characters.");

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("Relation Type is required.")
            .Must(x => x == "Lead" || x == "Customer").WithMessage("Relation Type must be 'Lead' or 'Customer'.");

        RuleFor(x => x.ReplyType)
            .NotEmpty().WithMessage("Reply Type is required.");
            
        RuleFor(x => x.OptionType)
            .NotEmpty().WithMessage("Option Type is required.")
            .Must(x => x == "ReplyButtons" || x == "CtaUrl" || x == "Files" || x == "PersonalAssistant")
            .WithMessage("Option Type must be 'ReplyButtons', 'CtaUrl', 'Files', or 'PersonalAssistant'.");
    }
}

public class UpdateMessageBotRequestValidator : AbstractValidator<UpdateMessageBotRequest>
{
    public UpdateMessageBotRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Name is required.")
            .MaximumLength(100).WithMessage("Bot Name cannot exceed 100 characters.");

        RuleFor(x => x.TriggerKeyword)
            .NotEmpty().WithMessage("Trigger Keyword is required.")
            .MaximumLength(256).WithMessage("Trigger Keyword cannot exceed 256 characters.");

        RuleFor(x => x.ReplyText)
            .NotEmpty().WithMessage("Reply Text is required.")
            .MaximumLength(1024).WithMessage("Reply Text cannot exceed 1024 characters.");

        RuleFor(x => x.RelationType)
            .NotEmpty().WithMessage("Relation Type is required.")
            .Must(x => x == "Lead" || x == "Customer").WithMessage("Relation Type must be 'Lead' or 'Customer'.");

        RuleFor(x => x.ReplyType)
            .NotEmpty().WithMessage("Reply Type is required.");
            
        RuleFor(x => x.OptionType)
            .NotEmpty().WithMessage("Option Type is required.")
            .Must(x => x == "ReplyButtons" || x == "CtaUrl" || x == "Files" || x == "PersonalAssistant")
            .WithMessage("Option Type must be 'ReplyButtons', 'CtaUrl', 'Files', or 'PersonalAssistant'.");
    }
}
