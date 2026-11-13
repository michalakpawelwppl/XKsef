using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using xKsef.CredentialService.Contracts;
using xKsef.CredentialService.Models;
using xKsef.CredentialService.Services;

namespace xKsef.CredentialService.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = CertificateAuthenticationDefaults.AuthenticationScheme)]
[Route("internal/ksef-credentials")]
public sealed class KsefCredentialsController : ControllerBase
{
    private readonly IKsefCredentialService _credentialService;
    private readonly IKsefTokenAuthenticationService _authenticationService;
    private readonly KsefCertificateService _certificateService;
    private readonly KsefCertificateAuthenticationService _certificateAuthenticationService;
    private readonly IKsefSessionService _sessionService;

    public KsefCredentialsController(IKsefCredentialService credentialService, IKsefTokenAuthenticationService authenticationService, KsefCertificateService certificateService, KsefCertificateAuthenticationService certificateAuthenticationService, IKsefSessionService sessionService)
    {
        _credentialService = credentialService;
        _authenticationService = authenticationService;
        _certificateService = certificateService;
        _certificateAuthenticationService = certificateAuthenticationService;
        _sessionService = sessionService;
    }
    
    [HttpPost("ensure-authenticated")]
    public async Task<IActionResult> EnsureAuthenticatedAsync([FromBody] AuthenticateKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest("Nie podano danych uwierzytelniania.");
        if (request.CredentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");
        if (request.TenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("Nie podano UserId.");
        if (string.IsNullOrWhiteSpace(request.Nip) || request.Nip.Length != 10 || !request.Nip.All(char.IsAsciiDigit)) return BadRequest("Nieprawidłowy NIP.");

        await _sessionService.EnsureAuthenticatedAsync(request.CredentialId, request.TenantId, request.UserId, request.Nip, cancellationToken);

        return Ok(new { Authenticated = true });
    }


    [HttpPost("token")]
    public async Task<IActionResult> SaveTokenAsync([FromBody] SaveKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("Brak UserId.");
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest("Token KSeF nie może być pusty.");

        byte[] tokenBytes = Encoding.UTF8.GetBytes(request.Token);

        try
        {
            Guid credentialId = await _credentialService.SaveAsync(request.TenantId, request.UserId, "Token", tokenBytes, cancellationToken);
            return Ok(new { CredentialId = credentialId });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    [HttpGet("token")]
    public async Task<IActionResult> GetTokenInfoAsync([FromQuery] Guid tenantId, [FromQuery] string userId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(userId)) return BadRequest("Brak UserId.");

        Guid? credentialId = await _credentialService.FindCredentialIdAsync(tenantId, userId, "Token", cancellationToken);

        return Ok(new { Exists = credentialId.HasValue, CredentialId = credentialId });
    }

    [HttpPost("authenticate-token")]
    public async Task<IActionResult> AuthenticateTokenAsync([FromBody] AuthenticateKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest("Nie podano danych uwierzytelniania.");
        if (request.CredentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");
        if (request.TenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("Nie podano UserId.");
        if (string.IsNullOrWhiteSpace(request.Nip) || request.Nip.Length != 10 || !request.Nip.All(char.IsAsciiDigit)) return BadRequest("Nieprawidłowy NIP.");

        await _authenticationService.AuthenticateAsync(request.CredentialId, request.TenantId, request.UserId, request.Nip, cancellationToken);

        return Ok(new { Authenticated = true });
    }

    [HttpGet("access-token")]
    public async Task<IActionResult> GetAccessTokenAsync([FromQuery] Guid tenantId, [FromQuery] string userId, [FromQuery] Guid credentialId, [FromQuery] string nip, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(userId)) return BadRequest("Brak UserId.");
        if (credentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");
        if (string.IsNullOrWhiteSpace(nip) || nip.Length != 10 || !nip.All(char.IsAsciiDigit)) return BadRequest("Nieprawidłowy NIP.");

        StoredSessionToken? storedToken = await _credentialService.GetAccessTokenAsync(tenantId, userId, credentialId, nip, cancellationToken);

        if (storedToken == null) return NotFound("Nie znaleziono sesji KSeF.");

        try
        {
            if (storedToken.ValidUntil <= DateTimeOffset.UtcNow.AddMinutes(2)) return Conflict("AccessToken wygasł lub wkrótce wygaśnie.");

            string accessToken = Encoding.UTF8.GetString(storedToken.TokenBytes);

            return Ok(new { AccessToken = accessToken, ValidUntil = storedToken.ValidUntil });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(storedToken.TokenBytes);
        }
    }

    [HttpGet("certificate")]
    public async Task<IActionResult> GetCertificateInfoAsync([FromQuery] Guid tenantId, [FromQuery] string userId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(userId)) return BadRequest("Brak UserId.");

        Guid? credentialId = await _credentialService.FindCredentialIdAsync(tenantId, userId, "Certificate", cancellationToken);

        return Ok(new { Exists = credentialId.HasValue, CredentialId = credentialId });
    }

    [HttpPost("certificate")]
    public async Task<IActionResult> SaveCertificateAsync([FromBody] SaveKsefCertificateRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest("Brak danych certyfikatu.");
        if (request.TenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("Brak UserId.");
        if (string.IsNullOrWhiteSpace(request.CertificatePem)) return BadRequest("Brak certyfikatu.");
        if (string.IsNullOrWhiteSpace(request.EncryptedPrivateKeyPem)) return BadRequest("Brak klucza prywatnego.");
        if (string.IsNullOrEmpty(request.Password)) return BadRequest("Brak hasła do klucza prywatnego.");

        try
        {
            Guid credentialId = await _certificateService.SaveCertificateAsync(request.TenantId, request.UserId, request.CertificatePem, request.EncryptedPrivateKeyPem, request.Password, cancellationToken);
            return Ok(new { CredentialId = credentialId });
        }
        catch (InvalidOperationException)
        {
            return BadRequest("Nie udało się zaimportować certyfikatu. Sprawdź poprawność certyfikatu, klucza prywatnego i hasła.");
        }
    }

    [HttpPost("authenticate-certificate")]
    public async Task<IActionResult> AuthenticateCertificateAsync([FromBody] AuthenticateKsefTokenRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest("Nie podano danych uwierzytelniania.");
        if (request.CredentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");
        if (request.TenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(request.UserId)) return BadRequest("Nie podano UserId.");
        if (string.IsNullOrWhiteSpace(request.Nip) || request.Nip.Length != 10 || !request.Nip.All(char.IsAsciiDigit)) return BadRequest("Nieprawidłowy NIP.");

        await _certificateAuthenticationService.AuthenticateAsync(request.CredentialId, request.TenantId, request.UserId, request.Nip, cancellationToken);

        return Ok(new { Authenticated = true });
    }


    [HttpGet("credential-type")]
    public async Task<IActionResult> GetCredentialTypeAsync([FromQuery] Guid credentialId, [FromQuery] Guid tenantId, [FromQuery] string userId, CancellationToken cancellationToken)
    {
        if (credentialId == Guid.Empty) return BadRequest("Nieprawidłowy CredentialId.");
        if (tenantId == Guid.Empty) return BadRequest("Nieprawidłowy TenantId.");
        if (string.IsNullOrWhiteSpace(userId)) return BadRequest("Nie podano UserId.");

        string? credentialType = await _credentialService.GetCredentialTypeAsync(credentialId, tenantId, userId, cancellationToken);

        if (credentialType == null) return NotFound("Nie znaleziono poświadczenia KSeF dla wskazanego użytkownika i tenanta.");

        return Ok(new { CredentialType = credentialType });
    }
}