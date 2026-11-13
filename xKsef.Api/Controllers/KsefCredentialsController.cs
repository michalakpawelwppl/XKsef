using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts.Credentials;
using xKsef.Application.Credentials;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(CredentialManagementAuthorizationFilter))]
[Route("api/ksef/credentials")]
public sealed class KsefCredentialsController : ControllerBase
{
    private readonly ICredentialServiceClient _credentialServiceClient;

    public KsefCredentialsController(ICredentialServiceClient credentialServiceClient)
    {
        _credentialServiceClient = credentialServiceClient;
    }

    [HttpPost("token")]
    public async Task<IActionResult> SaveTokenAsync([FromBody] SaveKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest("Token KSeF nie może być pusty.");

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        Guid credentialId = await _credentialServiceClient.SaveTokenAsync(tenantId, userId, request.Token, cancellationToken);

        return Ok(new { CredentialId = credentialId });
    }

    [HttpGet("token")]
    public async Task<ActionResult<KsefTokenInfo>> GetTokenInfoAsync(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        KsefTokenInfo result = await _credentialServiceClient.GetTokenInfoAsync(tenantId, userId, cancellationToken);

        return Ok(result);
    }

    [HttpGet("certificate-info")]
    public async Task<ActionResult<KsefTokenInfo>> GetCertificateInfoAsync(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        KsefTokenInfo result = await _credentialServiceClient.GetCertificateInfoAsync(tenantId, userId, cancellationToken);

        return Ok(result);
    }

}