namespace xKsef.Application.Authentication;

public interface IJwtTokenService
{
    string GenerateToken(
        string userId,
        string userName);
}