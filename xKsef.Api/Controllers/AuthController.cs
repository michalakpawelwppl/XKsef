using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using xKsef.Api.Contracts.Authentication;
using xKsef.Application.Authentication;
using xKsef.Contracts.Authentication;

namespace xKsef.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserAuthenticationService _authenticationService;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(IUserAuthenticationService authenticationService,IJwtTokenService jwtTokenService)
    {
        _authenticationService = authenticationService;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> LoginAsync(
    [FromBody] LoginRequest request,
    CancellationToken cancellationToken)
    {
        var user = await _authenticationService.AuthenticateAsync(
            request.Login,
            request.Password,
            cancellationToken);

        if (user is null)
        {
            return Unauthorized();
        }

        var token = _jwtTokenService.GenerateToken(
            user.Id,
            user.UserName);

        return Ok(new LoginResponse(
            AccessToken: token,
            TokenType: "Bearer"));
    }


}