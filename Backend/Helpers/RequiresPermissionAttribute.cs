using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WhatsAppCampaignApi.Models.DTOs.Common;
using WhatsAppCampaignApi.Services.Interfaces;

namespace WhatsAppCampaignApi.Helpers;

/// <summary>
/// Requires the caller to hold at least one of the given permission keys.
///
/// <code>[RequiresPermission("User.Create")]</code>
/// <code>[RequiresPermission("User.Edit", "User.Create")]  // any-of</code>
///
/// <para>
/// Implemented as a filter rather than an authorization policy: a dynamic policy provider would
/// need a policy-name parser, a requirement type and a handler to achieve the same thing, in a
/// codebase that has no authorization infrastructure to reuse. It is also deliberately not
/// enforced in the service layer — that would mean guarding 40+ methods with an easy one to
/// miss, and would push HTTP concerns into a data layer that currently has none.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresPermissionAttribute : TypeFilterAttribute
{
    public RequiresPermissionAttribute(params string[] permissionKeys)
        : base(typeof(RequiresPermissionFilter))
    {
        Arguments = new object[] { permissionKeys };
    }
}

/// <summary>Runs the permission check for <see cref="RequiresPermissionAttribute"/>.</summary>
public class RequiresPermissionFilter : IAsyncAuthorizationFilter
{
    private readonly string[] _permissionKeys;
    private readonly ICurrentUserService _currentUser;

    public RequiresPermissionFilter(string[] permissionKeys, ICurrentUserService currentUser)
    {
        _permissionKeys = permissionKeys;
        _currentUser = currentUser;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // An explicit [AllowAnonymous] anywhere on the endpoint wins.
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            return;

        if (!_currentUser.IsAuthenticated)
        {
            context.Result = new UnauthorizedObjectResult(new ApiResponse
            {
                Success = false,
                Message = "Authentication is required."
            });
            return;
        }

        if (await _currentUser.HasAnyPermissionAsync(_permissionKeys))
            return;

        // Name the missing permission. An admin reading the response should be able to go and
        // grant exactly the right checkbox without guessing.
        context.Result = new ObjectResult(new ApiResponse
        {
            Success = false,
            Message = _permissionKeys.Length == 1
                ? $"You do not have permission to perform this action ({_permissionKeys[0]})."
                : $"You do not have permission to perform this action. Requires one of: {string.Join(", ", _permissionKeys)}."
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };
    }
}
