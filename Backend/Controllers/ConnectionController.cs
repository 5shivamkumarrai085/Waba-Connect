using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Models.DTOs;
using WhatsAppCampaignApi.Services.Interfaces;

using WhatsAppCampaignApi.Helpers;

namespace WhatsAppCampaignApi.Controllers;

[ApiController]
[Route("api/connections")]
[Authorize]
public class ConnectionController : ControllerBase
{
    private readonly IConnectionService _connectionService;

    public ConnectionController(IConnectionService connectionService)
    {
        _connectionService = connectionService;
    }

    [HttpGet]
    [RequiresPermission("ConnectAccount.View")]
    public async Task<IActionResult> GetAll([FromQuery] string? userId, [FromQuery] string? departmentId)
    {
        var connections = await _connectionService.GetAllAsync(userId, departmentId);
        return Ok(connections);
    }

    [HttpGet("dashboard")]
    [RequiresPermission("ConnectAccount.View")]
    public async Task<IActionResult> GetDashboard([FromQuery] string? userId, [FromQuery] string? departmentId)
    {
        var dashboard = await _connectionService.GetDashboardAsync(userId, departmentId);
        return Ok(dashboard);
    }

    [HttpGet("{id}")]
    [RequiresPermission("ConnectAccount.View")]
    public async Task<IActionResult> GetById(int id)
    {
        var connection = await _connectionService.GetByIdAsync(id);
        if (connection == null) return NotFound(new { message = $"Connection with ID {id} not found." });
        return Ok(connection);
    }

    [HttpPost]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<IActionResult> Create([FromBody] CreateConnectionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var created = await _connectionService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    [RequiresPermission("ConnectAccount.Edit")]
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
    [RequiresPermission("ConnectAccount.Disconnect")]
    public async Task<IActionResult> Disconnect(int id)
    {
        var success = await _connectionService.SoftDisconnectAsync(id);
        return Ok(new { success, message = "Connection status set to disconnected." });
    }

    [HttpPost("{id}/reconnect")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<IActionResult> Reconnect(int id)
    {
        var success = await _connectionService.ReconnectAsync(id);
        return Ok(new { success, message = "Connection reconnected and sender numbers restored." });
    }

    /// <summary>
    /// Re-reads this connection's sender numbers from Meta.
    ///
    /// The repair for a connection that is authenticated but has no number attached — it looks
    /// configured, and can neither send nor receive. Before this the only way back was to run the
    /// whole connect wizard again.
    /// </summary>
    [HttpPost("{id}/sync-numbers")]
    [RequiresPermission("ConnectAccount.Connect")]
    public async Task<IActionResult> SyncNumbers(int id)
    {
        var count = await _connectionService.SyncPhoneNumbersAsync(id);

        return Ok(new
        {
            success = count > 0,
            count,
            message = count > 0
                ? $"{count} sender number(s) attached."
                : "Meta returned no phone numbers for this WhatsApp Business Account. Add a number to it in Meta Business Manager, then sync again."
        });
    }

    [HttpDelete("{id}")]
    [RequiresPermission("ConnectAccount.Delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var success = await _connectionService.SoftDeleteAsync(id);
        if (!success) return NotFound(new { message = $"Connection with ID {id} not found." });
        return Ok(new { success, message = "Connection marked inactive (soft deleted)." });
    }
}
