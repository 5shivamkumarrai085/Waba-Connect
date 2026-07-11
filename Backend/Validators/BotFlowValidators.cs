using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.BotFlow;

namespace WhatsAppCampaignApi.Validators;

public class CreateBotFlowRequestValidator : AbstractValidator<CreateBotFlowRequest>
{
    public CreateBotFlowRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Flow Name is required.")
            .MaximumLength(100).WithMessage("Bot Flow Name cannot exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}

public class UpdateBotFlowRequestValidator : AbstractValidator<UpdateBotFlowRequest>
{
    public UpdateBotFlowRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Bot Flow Name is required.")
            .MaximumLength(100).WithMessage("Bot Flow Name cannot exceed 100 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.");
    }
}
