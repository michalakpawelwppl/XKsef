using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants/{tenantId:guid}/users")]
public sealed class TenantUsersController(ITenantUsersService service) : ControllerBase
{
    private string? ActorId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> GetAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActorId)) return Unauthorized();
        var result = await service.GetAsync(tenantId, ActorId, cancellationToken);
        return result.Error is null ? Ok(result.Users) : Error(result.Error);
    }

    [HttpPost]
    public async Task<IActionResult> AddAsync(Guid tenantId, [FromBody] AddTenantUserRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActorId)) return Unauthorized();
        var result = await service.AddAsync(tenantId, ActorId, request.Identifier, request.Role, cancellationToken);
        return result.Error is null ? StatusCode(201, new TenantAccessChangeDto(result.CurrentUserRole)) : Error(result.Error);
    }

    [HttpPut("{userId}/role")]
    public async Task<IActionResult> SetRoleAsync(Guid tenantId, string userId, [FromBody] ChangeTenantUserRoleRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActorId)) return Unauthorized();
        var result = await service.SetRoleAsync(tenantId, ActorId, userId, request.Role, cancellationToken);
        return result.Error is null ? Ok(new TenantAccessChangeDto(result.CurrentUserRole)) : Error(result.Error);
    }

    [HttpDelete("{userId}")]
    public async Task<IActionResult> RemoveAsync(Guid tenantId, string userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ActorId)) return Unauthorized();
        var result = await service.RemoveAsync(tenantId, ActorId, userId, cancellationToken);
        return result.Error is null ? Ok(new TenantAccessChangeDto(result.CurrentUserRole)) : Error(result.Error);
    }

    private IActionResult Error(string code) => code switch
    {
        "Forbidden" => Forbid(),
        "UserNotFound" or "MemberNotFound" => StatusCode(404, new { Code = code }),
        "AlreadyMember" or "LastAdmin" or "AmbiguousUser" or "MembershipConflict" => StatusCode(409, new { Code = code }),
        _ => BadRequest(new { Code = "InvalidData" })
    };
}