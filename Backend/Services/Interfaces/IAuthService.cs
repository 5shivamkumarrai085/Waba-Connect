using WhatsAppCampaignApi.Models.DTOs.Auth;

namespace WhatsAppCampaignApi.Services.Interfaces;

public interface IAuthService
{
    /// <summary>
    /// Verifies credentials and issues an access token plus a refresh token. Every attempt,
    /// successful or not, is recorded as a LoginAttempt.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Bad email or password.</exception>
    /// <exception cref="InvalidOperationException">Account deactivated or temporarily locked.</exception>
    Task<LoginResponse> LoginAsync(LoginRequest request);

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token (rotation).
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Unknown, expired, revoked or replayed token.</exception>
    Task<LoginResponse> RefreshAsync(string refreshToken);

    /// <summary>Revokes one refresh token, or every session of the user when none is given.</summary>
    Task LogoutAsync(int userId, string? refreshToken);

    /// <summary>Builds the /auth/me payload, including freshly resolved permissions.</summary>
    Task<CurrentUserResponse> GetCurrentUserAsync(int userId);

    /// <summary>
    /// Changes the caller's own password after verifying the current one, clears the
    /// must-change-password flag, ends every other session and returns fresh tokens.
    /// </summary>
    Task<LoginResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request);
}
