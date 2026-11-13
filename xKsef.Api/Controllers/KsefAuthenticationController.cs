using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts;
using xKsef.Application.Credentials;
using xKsef.Application.Tenants;
using xKsef.Contracts.Authentication;
using xKsef.Infrastructure.Ksef.Authentication;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/ksef/authentication")]
public sealed class KsefAuthenticationController : ControllerBase
{
    private readonly KsefAuthenticationService _authenticationService;
    private readonly PendingKsefAuthenticationStore _pendingAuthenticationStore;
    private readonly ITenantService _tenantService;
    private readonly KsefAuthenticationXmlValidator _xmlValidator;
    private readonly ICredentialServiceClient _credentialServiceClient;

    public KsefAuthenticationController(KsefAuthenticationService authenticationService, ITenantService tenantService, PendingKsefAuthenticationStore pendingAuthenticationStore, KsefAuthenticationXmlValidator xmlValidator, ICredentialServiceClient credentialServiceClient)
    {
        _authenticationService = authenticationService;
        _tenantService = tenantService;
        _pendingAuthenticationStore = pendingAuthenticationStore;
        _xmlValidator = xmlValidator;
        _credentialServiceClient = credentialServiceClient;
    }


    [HttpPost("prepare")]
    public async Task<IActionResult> PrepareAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);

        if (taxEntity is null)
            return NotFound("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");

        var prepared = await _authenticationService.PrepareAuthenticationXmlAsync(taxEntity.Nip, cancellationToken);

        var pending = new PendingKsefAuthentication
        {
            UserId = userId,
            TenantId = tenantId,
            Nip = taxEntity.Nip,
            Challenge = prepared.Challenge
        };

        _pendingAuthenticationStore.Save(pending);

        return Ok(new
        {
            OperationId = pending.Id,
            Xml = prepared.Xml
        });
    }

    [HttpPost("submit")]
    public async Task<IActionResult> SubmitAsync([FromBody] SubmitKsefAuthenticationRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.SignedXml))
            return BadRequest("Brak podpisanego XML.");

        var pending = _pendingAuthenticationStore.Take(request.OperationId, userId, tenantId);

        if (pending is null)
            return BadRequest("Operacja uwierzytelniania nie istnieje, wygasła lub nie należy do bieżącego użytkownika i tenanta.");

        try
        {
            _xmlValidator.Validate(request.SignedXml, pending);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException or System.Security.Cryptography.CryptographicException or FormatException)
        {
            return BadRequest("Nieprawidłowy podpisany dokument uwierzytelniania KSeF.");
        }

        var operation = await _authenticationService.SubmitSignedAuthenticationXmlAsync(request.SignedXml, cancellationToken);
        await _authenticationService.CompleteAuthenticationAsync(operation, userId, tenantId, cancellationToken);
        return Ok(new { Success = true });
    }

    [HttpPost("authenticate-token")]
    public async Task<IActionResult> AuthenticateTokenAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
        if (taxEntity is null) return NotFound("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");

        KsefTokenInfo tokenInfo = await _credentialServiceClient.GetTokenInfoAsync(tenantId, userId, cancellationToken);
        if (!tokenInfo.Exists || tokenInfo.CredentialId is not Guid credentialId || credentialId == Guid.Empty) return BadRequest("Nie skonfigurowano tokenu KSeF dla bieżącego użytkownika i tenanta.");

        await _credentialServiceClient.AuthenticateTokenAsync(credentialId, tenantId, userId, taxEntity.Nip, cancellationToken);

        return Ok(new { Authenticated = true });
    }

  

    [HttpPost("certificate")]
    [Authorize]
    [ServiceFilter(typeof(TenantAuthorizationFilter))]
    public async Task<IActionResult> SaveCertificateAsync([FromBody] SaveKsefCertificateRequest request, CancellationToken cancellationToken)
    {
        if (!HttpContext.Items.TryGetValue("TenantId", out var tenantValue) || tenantValue is not Guid tenantId) return BadRequest("Nie wybrano tenanta.");

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        if (request == null) return BadRequest("Brak danych certyfikatu.");
        if (string.IsNullOrWhiteSpace(request.CertificatePem)) return BadRequest("Brak certyfikatu.");
        if (string.IsNullOrWhiteSpace(request.EncryptedPrivateKeyPem)) return BadRequest("Brak klucza prywatnego.");
        if (string.IsNullOrEmpty(request.Password)) return BadRequest("Brak hasła do klucza prywatnego.");

        Guid credentialId = await _credentialServiceClient.SaveCertificateAsync(tenantId, userId, request.CertificatePem, request.EncryptedPrivateKeyPem, request.Password, cancellationToken);

        return Ok(new { CredentialId = credentialId });
    }

    [HttpPost("authenticate-certificate")]
    public async Task<IActionResult> AuthenticateCertificateAsync(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId || tenantId == Guid.Empty) return Forbid();

        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
        if (taxEntity is null) return NotFound("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");

        KsefTokenInfo certificateInfo = await _credentialServiceClient.GetCertificateInfoAsync(tenantId, userId, cancellationToken);
        if (!certificateInfo.Exists || certificateInfo.CredentialId is not Guid credentialId || credentialId == Guid.Empty) return BadRequest("Nie skonfigurowano certyfikatu KSeF dla bieżącego użytkownika i tenanta.");

        await _credentialServiceClient.AuthenticateCertificateAsync(credentialId, tenantId, userId, taxEntity.Nip, cancellationToken);

        return Ok(new { Authenticated = true });
    }
}