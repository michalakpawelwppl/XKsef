using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using xKsef.Application.Credentials;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts.Tenants;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants")]
public sealed class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;
    private readonly ICredentialServiceClient _credentialServiceClient;

    public TenantsController(ITenantService tenantService, ICredentialServiceClient credentialServiceClient)
    {
        _tenantService = tenantService;
        _credentialServiceClient = credentialServiceClient;
    }

    [HttpPost]
    [Authorize(Policy = ApplicationOperatorAuthorization.PolicyName)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        CreateTenantResult result = await _tenantService.CreateAsync(request.Name, request.Nip, request.TaxEntityName, request.UserLogin, cancellationToken);
        if (result.TenantId is Guid tenantId)
            return StatusCode(StatusCodes.Status201Created, new { TenantId = tenantId });

        if (result.Error == "NipConflict")
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Konflikt zapisu podmiotu podatkowego."
            });
        }

        return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["UserLogin"] = new[] { "UserNotFound" }
        }));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TenantDto>>> GetMyTenants(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var tenants = await _tenantService.GetUserTenantsAsync(userId, cancellationToken);

        return Ok(tenants);
    }

    [HttpPut("{tenantId:guid}/preferred-ksef-credential")]
    public async Task<IActionResult> SetPreferredKsefCredentialAsync(Guid tenantId, [FromBody] SetPreferredKsefCredentialRequest request, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (request == null) return BadRequest("Nie podano danych poświadczenia.");
        if (request.CredentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");

        IReadOnlyList<TenantDto> tenants = await _tenantService.GetUserTenantsAsync(userId, cancellationToken);

        if (!tenants.Any(x => x.Id == tenantId)) return Forbid();

        if (request.CredentialId.HasValue)
        {
            var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
            if (taxEntity is null) return Forbid();
            if (taxEntity.Nip is null || taxEntity.Nip.Length != 10 || !taxEntity.Nip.All(char.IsAsciiDigit)) return Problem("Nieprawidłowy NIP podmiotu podatkowego.", statusCode: 500);
            string? credentialType = await _credentialServiceClient.GetCredentialTypeAsync(request.CredentialId.Value, tenantId, userId, taxEntity.Nip, cancellationToken);

            if (credentialType == null) return BadRequest("Poświadczenie KSeF nie istnieje lub nie należy do wskazanego użytkownika i tenanta.");
        }

        await _tenantService.SetPreferredKsefCredentialIdAsync(tenantId, userId, request.CredentialId, cancellationToken);

        return NoContent();
    }

    [HttpGet("{tenantId:guid}/preferred-ksef-credential")]
    public async Task<ActionResult<Guid?>> GetPreferredKsefCredentialAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");

        IReadOnlyList<TenantDto> tenants = await _tenantService.GetUserTenantsAsync(userId, cancellationToken);

        if (!tenants.Any(x => x.Id == tenantId)) return Forbid();

        Guid? credentialId = await _tenantService.GetPreferredKsefCredentialIdAsync(tenantId, userId, cancellationToken);

        return Ok(new { CredentialId = credentialId });
    }
}