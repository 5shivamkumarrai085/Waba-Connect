using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Templates;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Validators;

public class CreateTemplateValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .Length(2, 100).WithMessage("Name must be between 2 and 100 characters.")
            .Matches(@"^[a-z][a-z0-9_]*$").WithMessage("Name must be lowercase, numbers, and underscores only, starting with a letter.");

        RuleFor(x => x.Language)
            .NotEmpty().WithMessage("Language is required.")
            .Length(2, 10).WithMessage("Language must be a valid ISO 639-1 code.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.")
            .IsEnumName(typeof(TemplateCategory), false).WithMessage("Invalid Category.");

        RuleFor(x => x.TemplateType)
            .IsEnumName(typeof(TemplateType), false).WithMessage("Invalid Template Type.");

        RuleFor(x => x.BodyText)
            .NotEmpty().WithMessage("BodyText is required.")
            .MaximumLength(1024).WithMessage("BodyText cannot exceed 1024 characters.");

        RuleFor(x => x.HeaderContent)
            .MaximumLength(500).WithMessage("HeaderContent cannot exceed 500 characters.");

        RuleFor(x => x.FooterText)
            .MaximumLength(60).WithMessage("FooterText cannot exceed 60 characters.");

        RuleForEach(x => x.Variables).SetValidator(new TemplateVariableValidator());
    }
}

public class UpdateTemplateValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100).Matches(@"^[a-z][a-z0-9_]*$");
        RuleFor(x => x.Language).NotEmpty().Length(2, 10);
        RuleFor(x => x.Category).NotEmpty().IsEnumName(typeof(TemplateCategory), false);
        RuleFor(x => x.TemplateType).IsEnumName(typeof(TemplateType), false);
        RuleFor(x => x.BodyText).NotEmpty().MaximumLength(1024);
        RuleFor(x => x.HeaderContent).MaximumLength(500);
        RuleFor(x => x.FooterText).MaximumLength(60);
        RuleForEach(x => x.Variables).SetValidator(new TemplateVariableValidator());
    }
}

public class TemplateVariableValidator : AbstractValidator<TemplateVariableRequest>
{
    public TemplateVariableValidator()
    {
        RuleFor(x => x.Position).GreaterThan(0).WithMessage("Variable Position must be greater than 0.");
        RuleFor(x => x.SampleValue).MaximumLength(200).WithMessage("SampleValue cannot exceed 200 characters.");
    }
}
