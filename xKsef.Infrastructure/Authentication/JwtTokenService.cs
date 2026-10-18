using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using xKsef.Application.Authentication;
using System.IdentityModel.Tokens.Jwt;

namespace xKsef.Infrastructure.Authentication;

internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly string _signingKey;
    private readonly int _expirationMinutes;
    private readonly SymmetricSecurityKey _securityKey;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IConfiguration configuration)
    {
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Brak Jwt:Issuer.");

        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Brak Jwt:Audience.");

        _signingKey = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Brak Jwt:SigningKey.");

        _expirationMinutes = configuration.GetValue<int>(
            "Jwt:ExpirationMinutes");

        if (_expirationMinutes <= 0)
        {
            throw new InvalidOperationException(
                "Jwt:ExpirationMinutes musi być większe od zera.");
        }
        var keyBytes = Convert.FromBase64String(_signingKey);

        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Klucz JWT musi mieć co najmniej 256 bitów.");
        }

        _securityKey = new SymmetricSecurityKey(keyBytes);
        _signingCredentials = new SigningCredentials(
    _securityKey,
    SecurityAlgorithms.HmacSha256);
    }

    public string GenerateToken(
        string userId,
        string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, userId),
        new(JwtRegisteredClaimNames.UniqueName, userName),
        new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };

        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_expirationMinutes),
            signingCredentials: _signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}