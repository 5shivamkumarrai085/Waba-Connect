using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Auth;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICurrentUserService _currentUser;

    public AuthController(IAuthService authService, ICurrentUserService currentUser)
    {
        _authService = authService;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Exchanges credentials for a token.
    /// </summary>
    /// <remarks>
    /// Errors are mapped here rather than left to ExceptionHandlingMiddleware, which rewrites
    /// every UnauthorizedAccessException to a bare "Unauthorized" and maps
    /// InvalidOperationException to 409. A login screen needs to distinguish "wrong password"
    /// (retry) from "account deactivated" (contact an admin), and 403 is the right code for the
    /// latter.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    // Tighter than the global allowance. This is the endpoint worth guessing at, and it is cheap
    // to defend — nobody legitimately signs in twenty times a minute. Partitioned by client IP
    // here, since by definition there is no user yet.
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var result = await _authService.LoginAsync(request);
            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Signed in successfully.",
                Data = result
            });
        }
        catch (UnauthorizedAccessException)
        {
            // Deliberately identical for an unknown email and a wrong password — distinguishing
            // them would let anyone enumerate which addresses have accounts.
            return Unauthorized(new ApiResponse
            {
                Success = false,
                Message = "Invalid email or password."
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and refresh token. The presented token is
    /// single-use; replaying it revokes every session from the same sign-in.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var result = await _authService.RefreshAsync(request.RefreshToken);
            return Ok(new ApiResponse<LoginResponse> { Success = true, Data = result });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new ApiResponse { Success = false, Message = "Your session has ended. Please sign in again." });
        }
    }

    /// <summary>The signed-in user plus their freshly resolved permission set.</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized(new ApiResponse
            {
                Success = false,
                Message = "Authentication is required."
            });
        }

        var user = await _authService.GetCurrentUserAsync(userId.Value);
        return Ok(new ApiResponse<CurrentUserResponse> { Success = true, Data = user });
    }

    /// <summary>
    /// Changes the caller's own password. Reachable while MustChangePassword is set, since that
    /// is exactly the state this endpoint exists to clear.
    /// </summary>
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return Unauthorized(new ApiResponse
            {
                Success = false,
                Message = "Authentication is required."
            });
        }

        try
        {
            // New tokens: the old ones say "must change password" and belong to a session that
            // the password change has just ended.
            var tokens = await _authService.ChangePasswordAsync(userId.Value, request);
            return Ok(new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Password changed successfully.",
                Data = tokens
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Ends the session: the refresh token is revoked so it can never mint another access token.
    /// With no token in the body, every session of this user is ended.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request)
    {
        if (_currentUser.UserId is { } userId)
        {
            await _authService.LogoutAsync(userId, request?.RefreshToken);
        }

        return Ok(new ApiResponse { Success = true, Message = "Signed out." });
    }
}
