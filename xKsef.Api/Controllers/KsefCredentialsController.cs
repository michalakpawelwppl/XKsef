using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts.Credentials;
using xKsef.Application.Credentials;
using xKsef.Application.Tenants;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/ksef/credentials")]
public sealed class KsefCredentialsController : ControllerBase
{
    private readonly ICredentialServiceClient _credentialServiceClient;

    private readonly ITenantService _tenantService;

    public KsefCredentialsController(ICredentialServiceClient credentialServiceClient, ITenantService tenantService)
    {
        _credentialServiceClient = credentialServiceClient;
        _tenantService = tenantService;
    }

    [HttpPost("token")]
    [ServiceFilter(typeof(CredentialManagementAuthorizationFilter))]
    public async Task<IActionResult> SaveTokenAsync([FromBody] SaveKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest("Token KSeF nie może być pusty.");

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
        if (taxEntity is null) return NotFound("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");
        if (taxEntity.Nip is null || taxEntity.Nip.Length != 10 || !taxEntity.Nip.All(char.IsAsciiDigit))
            return Problem("Nieprawidłowy NIP podmiotu podatkowego wybranego tenanta.", statusCode: 500);

        Guid credentialId = await _credentialServiceClient.SaveTokenAsync(tenantId, userId, request.Token, taxEntity.Nip, cancellationToken);

        return Ok(new { CredentialId = credentialId });
    }

    [HttpGet("token")]
    [ServiceFilter(typeof(CredentialManagementAuthorizationFilter))]
    public async Task<ActionResult<KsefTokenInfo>> GetTokenInfoAsync(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
        if (taxEntity is null) return NotFound("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");
        if (taxEntity.Nip is null || taxEntity.Nip.Length != 10 || !taxEntity.Nip.All(char.IsAsciiDigit))
            return Problem("Nieprawidłowy NIP podmiotu podatkowego wybranego tenanta.", statusCode: 500);

        KsefTokenInfo result = await _credentialServiceClient.GetTokenInfoAsync(tenantId, userId, taxEntity.Nip, cancellationToken);

        return Ok(result);
    }

    [HttpGet("certificates")]
    public async Task<ActionResult<IReadOnlyList<KsefCertificateInfo>>> GetCertificatesAsync(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        return Ok(await _credentialServiceClient.GetCertificatesAsync(userId, cancellationToken));
    }
    [HttpGet("certificate-info")]
    public async Task<ActionResult<KsefTokenInfo>> GetCertificateInfoAsync(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        KsefTokenInfo result = await _credentialServiceClient.GetCertificateInfoAsync(userId, cancellationToken);

        return Ok(result);
    }

}