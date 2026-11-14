using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using xKsef.Api.Authentication;
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
    private readonly IUserCreationService _userCreationService;

    public AuthController(IUserAuthenticationService authenticationService,IJwtTokenService jwtTokenService, IUserCreationService userCreationService)
    {
        _authenticationService = authenticationService;
        _jwtTokenService = jwtTokenService;
        _userCreationService = userCreationService;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RegistrationRateLimiting.PolicyName)]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        CreateUserResult result = await _userCreationService.CreateAsync(request.Login.Trim(), request.Email.Trim(), request.Password, cancellationToken);
        if (result.UserId is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["Identity"] = result.Errors.ToArray() }));
        return StatusCode(StatusCodes.Status201Created, new RegisterResponse(result.UserId));
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