using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.MessageBot;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessageBotsController : ControllerBase
{
    private readonly IMessageBotService _botService;

    public MessageBotsController(IMessageBotService botService)
    {
        _botService = botService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<MessageBotResponse>>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? relationType = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false)
    {
        var request = new PagedRequest
        {
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            SortDescending = sortDescending
        };

        var data = await _botService.GetPagedAsync(request, relationType, isActive);
        return Ok(new ApiResponse<PagedResponse<MessageBotResponse>> { Success = true, Data = data });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<MessageBotResponse>>> GetById(int id)
    {
        try
        {
            var data = await _botService.GetByIdAsync(id);
            return Ok(new ApiResponse<MessageBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<MessageBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MessageBotResponse>>> Create([FromBody] CreateMessageBotRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<MessageBotResponse> { Success = false, Message = "Validation failed." });
        }

        var data = await _botService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = data.Id }, new ApiResponse<MessageBotResponse> { Success = true, Data = data });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<MessageBotResponse>>> Update(int id, [FromBody] UpdateMessageBotRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiResponse<MessageBotResponse> { Success = false, Message = "Validation failed." });
        }

        try
        {
            var data = await _botService.UpdateAsync(id, request);
            return Ok(new ApiResponse<MessageBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<MessageBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await _botService.DeleteAsync(id);
        if (!result)
        {
            return NotFound(new ApiResponse<bool> { Success = false, Message = $"Message bot with ID {id} not found." });
        }
        return Ok(new ApiResponse<bool> { Success = true, Data = true });
    }

    [HttpPost("clone/{id}")]
    public async Task<ActionResult<ApiResponse<MessageBotResponse>>> Clone(int id)
    {
        try
        {
            var data = await _botService.CloneAsync(id);
            return Ok(new ApiResponse<MessageBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<MessageBotResponse> { Success = false, Message = ex.Message });
        }
    }

    [HttpPatch("toggle/{id}")]
    public async Task<ActionResult<ApiResponse<MessageBotResponse>>> ToggleActive(int id)
    {
        try
        {
            var data = await _botService.ToggleActiveAsync(id);
            return Ok(new ApiResponse<MessageBotResponse> { Success = true, Data = data });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ApiResponse<MessageBotResponse> { Success = false, Message = ex.Message });
        }
    }
}
