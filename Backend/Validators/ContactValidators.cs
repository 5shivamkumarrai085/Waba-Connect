using FluentValidation;
using System.Text.RegularExpressions;
using WhatsAppCampaignApi.Models.DTOs.Contacts;
using WhatsAppCampaignApi.Models.Enums;

namespace WhatsAppCampaignApi.Validators;

public class CreateContactValidator : AbstractValidator<CreateContactRequest>
{
    public CreateContactValidator()
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
            .IsEnumName(typeof(ContactStatus), caseSensitive: false).WithMessage("Invalid Contact Status.");

        RuleFor(x => x.Source)
            .NotEmpty().WithMessage("Source is required.")
            .IsEnumName(typeof(ContactSource), caseSensitive: false).WithMessage("Invalid Contact Source.");

        RuleFor(x => x.AssignedTo)
            .MaximumLength(100).WithMessage("AssignedTo cannot exceed 100 characters.");

        RuleForEach(x => x.GroupIds)
            .GreaterThan(0).WithMessage("Group IDs must be greater than 0.")
            .When(x => x.GroupIds != null);
    }
}

public class UpdateContactValidator : AbstractValidator<UpdateContactRequest>
{
    public UpdateContactValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Phone).NotEmpty().Matches(@"^\+[1-9]\d{6,14}$");
        RuleFor(x => x.Type).NotEmpty().IsEnumName(typeof(ContactType), false);
        RuleFor(x => x.Status).NotEmpty().IsEnumName(typeof(ContactStatus), false);
        RuleFor(x => x.Source).NotEmpty().IsEnumName(typeof(ContactSource), false);
        RuleFor(x => x.AssignedTo).MaximumLength(100);
        RuleForEach(x => x.GroupIds).GreaterThan(0).When(x => x.GroupIds != null);
    }
}
