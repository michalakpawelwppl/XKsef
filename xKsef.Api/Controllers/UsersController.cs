using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Api.Contracts.Users;
using xKsef.Application.Authentication;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize(Policy = ApplicationOperatorAuthorization.PolicyName)]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserCreationService _userCreationService;

    public UsersController(IUserCreationService userCreationService)
    {
        _userCreationService = userCreationService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        CreateUserResult result = await _userCreationService.CreateAsync(request.Login, request.Email, request.Password, cancellationToken);
        if (result.UserId is null)
        {
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Identity"] = result.Errors.ToArray()
            }));
        }

        return StatusCode(StatusCodes.Status201Created, new { UserId = result.UserId });
    }
}
