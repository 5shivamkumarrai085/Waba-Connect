using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Helpers;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Models.DTOs.Setup;

namespace WhatsAppCampaignApi.Controllers;

/// <summary>
/// Serves the permission catalogue that drives the Features × Capabilities matrix.
///
/// Routed under api/permission-catalog rather than api/permissions to avoid colliding with the
/// pre-existing PermissionController, which handles WABA connection scoping — a different axis
/// that happens to share the word "permission".
/// </summary>
[ApiController]
[Route("api/permission-catalog")]
[Authorize]
public class PermissionsCatalogController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public PermissionsCatalogController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// The catalogue grouped for display. Capabilities are returned per-feature exactly as
    /// seeded, so the matrix never renders a checkbox that grants nothing.
    /// </summary>
    // Any-of: the matrix is rendered by the role form and by the user form's custom-permissions
    // override, so either permission is enough to need the catalogue's shape.
    [HttpGet]
    [RequiresPermission("Role.View", "User.View")]
    public async Task<IActionResult> GetCatalog()
    {
        var permissions = await _dbContext.Permissions
            .AsNoTracking()
            .OrderBy(p => p.SortOrder)
            .ToListAsync();

        var groups = permissions
            .GroupBy(p => p.GroupName ?? "Other")
            .Select(group => new PermissionGroupResponse
            {
                Name = group.Key,
                Features = group
                    .GroupBy(p => p.Feature)
                    .Select(feature => new PermissionFeatureResponse
                    {
                        Feature = feature.Key,
                        DisplayName = feature.First().FeatureDisplayName,
                        Capabilities = feature
                            .OrderBy(p => p.SortOrder)
                            .Select(p => new PermissionCapabilityResponse
                            {
                                Id = p.Id,
                                Key = p.Key,
                                Capability = p.Capability,
                                DisplayName = p.CapabilityDisplayName
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return Ok(new ApiResponse<PermissionCatalogResponse>
        {
            Success = true,
            Data = new PermissionCatalogResponse { Groups = groups }
        });
    }
}
