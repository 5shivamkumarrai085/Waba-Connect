using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/connections")]
public class ConnectionController : ControllerBase
{
    private readonly IConnectionService _connectionService;

    public ConnectionController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? userId, [FromQuery] string? departmentId)
    {
        var connections = await _connectionService.GetAllAsync(userId, departmentId);
        return Ok(connections);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] string? userId, [FromQuery] string? departmentId)
    {
        var dashboard = await _connectionService.GetDashboardAsync(userId, departmentId);
        return Ok(dashboard);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var connection = await _connectionService.GetByIdAsync(id);
        if (connection == null) return NotFound(new { message = $"Connection with ID {id} not found." });
        return Ok(connection);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConnectionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var created = await _connectionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateConnectionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var updated = await _connectionService.UpdateAsync(id, request);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = $"Connection with ID {id} not found." });
        }
    }

    [HttpPost("{id}/disconnect")]
    public async Task<IActionResult> Disconnect(int id)
    {
        var success = await _connectionService.SoftDisconnectAsync(id);
        return Ok(new { success, message = "Connection status set to disconnected." });
    }

    [HttpPost("{id}/reconnect")]
    public async Task<IActionResult> Reconnect(int id)
    {
        var success = await _connectionService.ReconnectAsync(id);
        return Ok(new { success, message = "Connection status set to connected." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _connectionService.SoftDeleteAsync(id);
        if (!success) return NotFound(new { message = $"Connection with ID {id} not found." });
        return Ok(new { success, message = "Connection marked inactive (soft deleted)." });
    }
}
