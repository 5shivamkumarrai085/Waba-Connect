using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Groups;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ContactGroupsController : ControllerBase
{
    private readonly IContactGroupService _groupService;

    public ContactGroupsController(IContactGroupService groupService)
    {
        _groupService = groupService;
    }

    [HttpGet]
    [RequiresPermission("ContactGroup.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<GroupResponse>>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null)
    {
        var request = new PagedRequest { Page = page, PageSize = pageSize, Search = search };
        var data = await _groupService.GetAllAsync(request);
        return Ok(new ApiResponse<PagedResponse<GroupResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    [RequiresPermission("ContactGroup.View")]
    public async Task<ActionResult<ApiResponse<GroupResponse>>> GetById(int id)
    {
        var data = await _groupService.GetByIdAsync(id);
        return Ok(new ApiResponse<GroupResponse> { Success = true, Data = data });
    }

    [HttpPost]
    [RequiresPermission("ContactGroup.Create")]
    public async Task<ActionResult<ApiResponse<GroupResponse>>> Create([FromBody] CreateGroupRequest request)
    {
        var data = await _groupService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<GroupResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    [RequiresPermission("ContactGroup.Edit")]
    public async Task<ActionResult<ApiResponse<GroupResponse>>> Update(int id, [FromBody] UpdateGroupRequest request)
    {
        var data = await _groupService.UpdateAsync(id, request);
        return Ok(new ApiResponse<GroupResponse> { Success = true, Data = data });
    }

    [HttpDelete("{id}")]
    [RequiresPermission("ContactGroup.Delete")]
    public async Task<ActionResult<ApiResponse>> Delete(int id)
    {
        await _groupService.DeleteAsync(id);
        return Ok(new ApiResponse { Success = true, Message = "Group deleted successfully." });
    }

    [HttpPost("{id}/members")]
    [RequiresPermission("ContactGroup.Edit")]
    public async Task<ActionResult<ApiResponse>> AddMembers(int id, [FromBody] GroupMembersRequest request)
    {
        await _groupService.AddMembersAsync(id, request);
        return Ok(new ApiResponse { Success = true, Message = "Members added successfully." });
    }

    [HttpDelete("{id}/members")]
    [RequiresPermission("ContactGroup.Edit")]
    public async Task<ActionResult<ApiResponse>> RemoveMembers(int id, [FromBody] GroupMembersRequest request)
    {
        await _groupService.RemoveMembersAsync(id, request);
        return Ok(new ApiResponse { Success = true, Message = "Members removed successfully." });
    }
}
