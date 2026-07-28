using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/permissions")]
public class PermissionController : ControllerBase
{
    private readonly IPermissionManagementService _permissionService;

    public PermissionController(IPermissionManagementService permissionService)
    {
        _permissionService = permissionService;
    }

    [HttpGet("user/dashboard")]
    public async Task<IActionResult> GetUserDashboard()
    {
        var data = await _permissionService.GetUserPermissionsDashboardAsync();
        return Ok(data);
    }

    [HttpGet("user")]
    public async Task<IActionResult> GetUserPermissions([FromQuery] string? department, [FromQuery] int? connectionId, [FromQuery] bool? activeOnly)
    {
        var data = await _permissionService.GetUserPermissionsAsync(department, connectionId, activeOnly);
        return Ok(data);
    }

    [HttpPost("user/assign")]
    public async Task<IActionResult> AssignUserPermission([FromBody] AssignUserPermissionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _permissionService.AssignUserPermissionAsync(request);
        return Ok(result);
    }

    [HttpPost("user/{id}/toggle")]
    public async Task<IActionResult> ToggleUserPermissionStatus(int id)
    {
        var success = await _permissionService.ToggleUserPermissionStatusAsync(id);
        return Ok(new { success });
    }

    [HttpDelete("user/{id}")]
    public async Task<IActionResult> DeleteUserPermission(int id)
    {
        var success = await _permissionService.DeleteUserPermissionAsync(id);
        return Ok(new { success });
    }

    [HttpGet("department/dashboard")]
    public async Task<IActionResult> GetDepartmentDashboard()
    {
        var data = await _permissionService.GetDepartmentPermissionsDashboardAsync();
        return Ok(data);
    }

    [HttpGet("department")]
    public async Task<IActionResult> GetDepartmentPermissions([FromQuery] int? connectionId, [FromQuery] bool? activeOnly)
    {
        var data = await _permissionService.GetDepartmentPermissionsAsync(connectionId, activeOnly);
        return Ok(data);
    }

    [HttpPost("department/assign")]
    public async Task<IActionResult> AssignDepartmentPermission([FromBody] AssignDepartmentPermissionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _permissionService.AssignDepartmentPermissionAsync(request);
        return Ok(result);
    }

    [HttpPost("department/{id}/toggle")]
    public async Task<IActionResult> ToggleDepartmentPermissionStatus(int id)
    {
        var success = await _permissionService.ToggleDepartmentPermissionStatusAsync(id);
        return Ok(new { success });
    }

    [HttpDelete("department/{id}")]
    public async Task<IActionResult> DeleteDepartmentPermission(int id)
    {
        var success = await _permissionService.DeleteDepartmentPermissionAsync(id);
        return Ok(new { success });
    }
}
