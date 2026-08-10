using FluentValidation;
using WhatsAppCampaignApi.Models.DTOs.Auth;

namespace WhatsAppCampaignApi.Validators;

/// <remarks>
/// Picked up automatically — Program.cs scans this assembly from CreateContactValidator, so no
/// registration is needed for new validators in this folder.
/// </remarks>
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        // Only a presence check. Length/complexity rules belong on the password-setting path;
        // enforcing them at login would reject legitimate older passwords and, worse, leak
        // which rules an account's password satisfies.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Current password is required.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128).WithMessage("Password must be 128 characters or fewer.")
            .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Please confirm the new password.")
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
    }
}
