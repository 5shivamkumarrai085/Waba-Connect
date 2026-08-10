using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/setup/roles")]
[Authorize]
public class RolesController : ControllerBase
{
    private readonly IRoleService _roleService;

    public RolesController(IRoleService roleService)
    {
        _roleService = roleService;
    }

    /// <summary>
    /// Also requires User.View: the user form's Role dropdown needs this list, and anyone who
    /// can manage users must be able to populate it without holding Role.View outright.
    /// </summary>
    [HttpGet]
    [RequiresPermission("Role.View", "User.View")]
    public async Task<IActionResult> GetAll()
    {
        var data = await _roleService.GetAllAsync();
        return Ok(new ApiResponse<List<RoleListItemResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id:int}")]
    [RequiresPermission("Role.View")]
    public async Task<IActionResult> GetById(int id)
    {
        var data = await _roleService.GetByIdAsync(id);
        return Ok(new ApiResponse<RoleDetailResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("Role.Create")]
    public async Task<IActionResult> Create([FromBody] CreateRoleRequest request)
    {
        var data = await _roleService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id },
            new ApiResponse<RoleDetailResponse> { Success = true, Message = "Role created successfully.", Data = data });
    }

    [HttpPut("{id:int}")]
    [RequiresPermission("Role.Edit")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateRoleRequest request)
    {
        var data = await _roleService.UpdateAsync(id, request);
        return Ok(new ApiResponse<RoleDetailResponse> { Success = true, Message = "Role updated successfully.", Data = data });
    }

    [HttpDelete("{id:int}")]
    [RequiresPermission("Role.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        await _roleService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Role deleted successfully." });
    }
}
