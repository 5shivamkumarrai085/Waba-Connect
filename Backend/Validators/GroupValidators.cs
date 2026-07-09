using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Groups;

namespace WhatsAppCampaignApi.Validators;

public class CreateGroupValidator : AbstractValidator<CreateGroupRequest>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class UpdateGroupValidator : AbstractValidator<UpdateGroupRequest>
{
    public UpdateGroupValidator()
    {
        RuleFor(x => x.Name).NotEmpty().Length(2, 100);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

public class GroupMembersValidator : AbstractValidator<GroupMembersRequest>
{
    public GroupMembersValidator()
    {
        RuleFor(x => x.ContactIds)
            .NotEmpty().WithMessage("ContactIds cannot be empty.");
            
        RuleForEach(x => x.ContactIds)
            .GreaterThan(0).WithMessage("Contact IDs must be greater than 0.");
    }
}
