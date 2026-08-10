using System.Threading.Tasks;
using WhatsAppCampaignApi.Models.DTOs.Auth;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Verifies credentials and issues a token. Every attempt, successful or not, is recorded as
    /// a LoginAttempt.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Bad email or password.</exception>
    /// <exception cref="InvalidOperationException">Account exists but is deactivated.</exception>
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>Builds the /auth/me payload, including freshly resolved permissions.</summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(int userId);

    /// <summary>
    /// Changes the caller's own password after verifying the current one, and clears the
    /// must-change-password flag.
    /// </summary>
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request);
}
