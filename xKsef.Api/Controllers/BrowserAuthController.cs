using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using xKsef.Api.Authentication;
using xKsef.Api.Contracts.Authentication;
using xKsef.Application.Authentication;
using xKsef.Contracts.Authentication;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Controllers;

[ApiController]
[BrowserRequest]
[Route("api/browser-auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BrowserAuthController(IUserAuthenticationService authentication, IUserCreationService users, IAntiforgery antiforgery,
    BrowserTicketStore sessions, BrowserSignInService signIn) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { RequestToken = tokens.RequestToken, HeaderName = BrowserAuthentication.CsrfHeader });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RegistrationRateLimiting.PolicyName)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(request.Login.Trim(), request.Email.Trim(), request.Password, cancellationToken);
        if (result.UserId is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["Identity"] = result.Errors.ToArray() }));
        return StatusCode(201, new RegisterResponse(result.UserId));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await authentication.AuthenticateAsync(request.Login, request.Password, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.SecurityStamp)) return Unauthorized();
        // Rotate an existing browser session; every successful login creates a new random server session key.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName), new Claim("AspNet.Identity.SecurityStamp", user.SecurityStamp) }, BrowserSessionOptions.AuthenticationScheme));
        await signIn.SignInAsync(HttpContext, principal);
        return Ok(new { UserId = user.Id, Login = user.UserName });
    }

    [HttpGet("me")]
    [Authorize(Policy = BrowserAuthentication.BrowserPolicy)]
    public IActionResult Me() => Ok(new { UserId = User.FindFirstValue(ClaimTypes.NameIdentifier), Login = User.Identity!.Name });

    [HttpPost("logout")]
    [Authorize(Policy = BrowserAuthentication.BrowserPolicy)]
    public async Task<IActionResult> LogoutAsync()
    {
        await HttpContext.SignOutAsync(BrowserSessionOptions.AuthenticationScheme);
        return NoContent();
    }

    [HttpPost("logout-all")]
    [Authorize(Policy = BrowserAuthentication.BrowserPolicy)]
    public async Task<IActionResult> LogoutAllAsync(CancellationToken cancellationToken)
    {
        await sessions.RevokeAllAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
        await HttpContext.SignOutAsync(BrowserSessionOptions.AuthenticationScheme);
        return NoContent();
    }
}
