using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Uno.Api.Authentication;

public sealed class JwtSettings(string key)
{
    public const string Issuer = "Uno.Api";
    public const string Audience = "Uno.Players";
    public SymmetricSecurityKey SigningKey { get; } = new(Encoding.UTF8.GetBytes(key));
    public string Issue(Guid userId)
    {
        var token = new JwtSecurityToken(Issuer, Audience,
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()) },
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public static string NewRefreshToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
