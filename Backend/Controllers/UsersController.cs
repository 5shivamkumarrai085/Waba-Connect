using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Internal staff accounts. Distinct from ContactsController, which manages the customers the
/// business messages on WhatsApp.
/// </summary>
[ApiController]
[Route("api/setup/users")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly WhatsAppCampaignApi.Services.Storage.IFileStorage _fileStorage;

    private static readonly string[] AllowedAvatarExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxAvatarBytes = 2 * 1024 * 1024;

    public UsersController(IUserService userService, WhatsAppCampaignApi.Services.Storage.IFileStorage fileStorage)
    {
        _userService = userService;
        _fileStorage = fileStorage;
    }

    [HttpGet]
    [RequiresPermission("User.View")]
    public async Task<IActionResult> GetAll(
        [FromQuery] PagedRequest request,
        [FromQuery] bool? isActive,
        [FromQuery] int? roleId)
    {
        var data = await _userService.GetAllAsync(request, isActive, roleId);
        return Ok(new ApiResponse<PagedResponse<UserListItemResponse>> { Success = true, Data = data });
    }

    [HttpGet("dashboard")]
    [RequiresPermission("User.View")]
    public async Task<IActionResult> GetDashboard()
    {
        var data = await _userService.GetDashboardAsync();
        return Ok(new ApiResponse<UserDashboardResponse> { Success = true, Data = data });
    }

    /// <summary>
    /// Lightweight list for "assigned to" pickers. Deliberately only requires Contact.View —
    /// an agent needs to see who a contact is assigned to without being able to manage users.
    /// </summary>
    [HttpGet("assignable")]
    [RequiresPermission("Contact.View", "User.View")]
    public async Task<IActionResult> GetAssignable()
    {
        var data = await _userService.GetAssignableAsync();
        return Ok(new ApiResponse<List<AssignableUserResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("User.View")]
    public async Task<IActionResult> GetById(int id)
    {
        var data = await _userService.GetByIdAsync(id);
        return Ok(new ApiResponse<UserDetailResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("User.Create")]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        var data = await _userService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id },
            new ApiResponse<UserDetailResponse> { Success = true, Message = "User created successfully.", Data = data });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("User.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        var data = await _userService.UpdateAsync(id, request);
        return Ok(new ApiResponse<UserDetailResponse> { Success = true, Message = "User updated successfully.", Data = data });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("User.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _userService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "User deleted successfully." });
    }

    [HttpPatch("{id:int}/toggle-active")]
    [RequiresPermission("User.Edit")]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var data = await _userService.ToggleActiveAsync(id);
        return Ok(new ApiResponse<UserListItemResponse>
        {
            Success = true,
            Message = data.IsActive ? "User activated." : "User deactivated.",
            Data = data
        });
    }

    /// <summary>
    /// Stores a profile image and returns its URL for the caller to save on the user record.
    /// Mirrors the existing campaign media upload rather than introducing a second approach.
    /// </summary>
    [HttpPost("upload-avatar")]
    [RequiresPermission("User.Create", "User.Edit")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new ApiResponse { Success = false, Message = "No file was uploaded." });

        if (file.Length > MaxAvatarBytes)
            return BadRequest(new ApiResponse { Success = false, Message = "Profile image must be 2 MB or smaller." });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAvatarExtensions.Contains(extension))
        {
            return BadRequest(new ApiResponse
            {
                Success = false,
                Message = $"Unsupported image type. Allowed: {string.Join(", ", AllowedAvatarExtensions)}."
            });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var stored = await _fileStorage.SavePublicMediaAsync(stream, file.FileName, file.ContentType, "avatars", HttpContext.RequestAborted);

            return Ok(new ApiResponse<string>
            {
                Success = true,
                Message = "Image uploaded.",
                Data = stored.RelativeUrl
            });
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new ApiResponse { Success = false, Message = ex.Message });
        }
    }
}
