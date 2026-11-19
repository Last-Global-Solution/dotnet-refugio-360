using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Refugio360.Api;

internal sealed class JwtSettings(string issuer, string audience, SymmetricSecurityKey signingKey)
{
    public string Issuer { get; } = issuer;
    public string Audience { get; } = audience;
    public SymmetricSecurityKey SigningKey { get; } = signingKey;

    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("REFUGIO_JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Configure Jwt:Secret (at least 32 UTF-8 bytes) using user secrets, Jwt__Secret or REFUGIO_JWT_SECRET.");

        return new JwtSettings(
            configuration["Jwt:Issuer"] ?? "Refugio360",
            configuration["Jwt:Audience"] ?? "Refugio360.Mobile",
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)));
    }
}
