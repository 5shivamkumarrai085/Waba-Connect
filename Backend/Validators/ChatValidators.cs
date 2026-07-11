using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Chat;

namespace WhatsAppCampaignApi.Validators;

public class SendChatMessageValidator : AbstractValidator<SendChatMessageRequest>
{
    public SendChatMessageValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Message text is required.")
            .MaximumLength(4096).WithMessage("Message text cannot exceed 4096 characters.");

        RuleFor(x => x.FromPhoneNumberId)
            .MaximumLength(100).WithMessage("FromPhoneNumberId cannot exceed 100 characters.");
    }
}
