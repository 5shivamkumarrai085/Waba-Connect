using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Validators;

public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100).WithMessage("First name must be 100 characters or fewer.");

        RuleFor(x => x.LastName)
            .MaximumLength(100).WithMessage("Last name must be 100 characters or fewer.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.PhoneNumber)
            .MaximumLength(30).WithMessage("Phone number must be 30 characters or fewer.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128)
            .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords do not match.");

        // A non-administrator with neither a role nor custom permissions can sign in but reach
        // nothing, which reads as a broken account rather than a deliberate one.
        RuleFor(x => x)
            .Must(x => x.IsAdministrator || x.RoleId.HasValue || x.UsesCustomPermissions)
            .WithMessage("Assign a role, enable custom permissions, or grant administrator access.")
            .WithName("Role");
    }
}

/// <summary>
/// Update rules mirror create, except the password is optional — an admin editing a phone
/// number shouldn't have to reset the person's password to save the form.
/// </summary>
public class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(100);

        RuleFor(x => x.LastName).MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(256);

        RuleFor(x => x.PhoneNumber).MaximumLength(30);

        When(x => !string.IsNullOrWhiteSpace(x.Password), () =>
        {
            RuleFor(x => x.Password)
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .MaximumLength(128)
                .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
                .Matches("[0-9]").WithMessage("Password must contain at least one number.");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("Passwords do not match.");
        });

        RuleFor(x => x)
            .Must(x => x.IsAdministrator || x.RoleId.HasValue || x.UsesCustomPermissions)
            .WithMessage("Assign a role, enable custom permissions, or grant administrator access.")
            .WithName("Role");
    }
}

public class CreateRoleRequestValidator : AbstractValidator<CreateRoleRequest>
{
    public CreateRoleRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100).WithMessage("Role name must be 100 characters or fewer.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must be 500 characters or fewer.");

        // A role granting nothing isn't useful, and assigning one silently strands the user.
        RuleFor(x => x)
            .Must(x => x.IsAdministrator || x.PermissionKeys.Count > 0)
            .WithMessage("Select at least one permission, or grant administrator access.")
            .WithName("Permissions");
    }
}

public class UpdateRoleRequestValidator : AbstractValidator<UpdateRoleRequest>
{
    public UpdateRoleRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Role name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Description).MaximumLength(500);

        RuleFor(x => x)
            .Must(x => x.IsAdministrator || x.PermissionKeys.Count > 0)
            .WithMessage("Select at least one permission, or grant administrator access.")
            .WithName("Permissions");
    }
}
