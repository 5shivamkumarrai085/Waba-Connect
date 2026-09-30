using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Segments;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>Dynamic, rule-based audiences.</summary>
[ApiController]
[Authorize]
[Route("api/segments")]
public class SegmentsController : ControllerBase
{
    private readonly ISegmentService _segments;

    public SegmentsController(ISegmentService segments) => _segments = segments;

    [HttpGet]
    [RequiresPermission("Segment.View")]
    public async Task<ActionResult<ApiResponse<PagedResponse<SegmentResponse>>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] string? search = null, CancellationToken ct = default) =>
        Ok(new ApiResponse<PagedResponse<SegmentResponse>> { Success = true, Data = await _segments.GetAllAsync(page, pageSize, search, ct) });

    [HttpGet("{id:int}")]
    [RequiresPermission("Segment.View")]
    public async Task<ActionResult<ApiResponse<SegmentResponse>>> Get(int id, CancellationToken ct) =>
        Ok(new ApiResponse<SegmentResponse> { Success = true, Data = await _segments.GetByIdAsync(id, ct) });

    /// <summary>The fields and operators a rule may use, for the builder.</summary>
    [HttpGet("fields")]
    [RequiresPermission("Segment.View")]
    public ActionResult<ApiResponse<IReadOnlyCollection<string>>> Fields() =>
        Ok(new ApiResponse<IReadOnlyCollection<string>> { Success = true, Data = SegmentQueryBuilder.Fields });

    /// <summary>How many contacts match these rules right now, with a sample. Nothing is saved.</summary>
    [HttpPost("preview")]
    [RequiresPermission("Segment.View")]
    public async Task<ActionResult<ApiResponse<SegmentPreviewResponse>>> Preview([FromBody] SegmentRuleGroup rules, CancellationToken ct) =>
        Ok(new ApiResponse<SegmentPreviewResponse> { Success = true, Data = await _segments.PreviewAsync(rules, ct) });

    [HttpPost]
    [RequiresPermission("Segment.Manage")]
    public async Task<ActionResult<ApiResponse<SegmentResponse>>> Create([FromBody] SaveSegmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<SegmentResponse> { Success = true, Data = await _segments.CreateAsync(request, ct), Message = "Segment created." });

    [HttpPut("{id:int}")]
    [RequiresPermission("Segment.Manage")]
    public async Task<ActionResult<ApiResponse<SegmentResponse>>> Update(int id, [FromBody] SaveSegmentRequest request, CancellationToken ct) =>
        Ok(new ApiResponse<SegmentResponse> { Success = true, Data = await _segments.UpdateAsync(id, request, ct), Message = "Segment saved." });

    [HttpDelete("{id:int}")]
    [RequiresPermission("Segment.Manage")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct)
    {
        await _segments.DeleteAsync(id, ct);
        return Ok(new ApiResponse<bool> { Success = true, Data = true, Message = "Segment deleted." });
    }
}
