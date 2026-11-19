using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Refugio360.Api;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class JwtSettingsTests
{
    [Fact]
    public void Weak_secret_is_rejected_even_if_legacy_environment_variable_exists()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "curta"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => JwtSettings.FromConfiguration(config));
    }

    [Fact]
    public void External_strong_secret_uses_configured_issuer_and_audience()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Jwt:Issuer"] = "Issuer.Teste",
            ["Jwt:Audience"] = "Audience.Teste"
        }).Build();

        var settings = JwtSettings.FromConfiguration(config);
        Assert.Equal("Issuer.Teste", settings.Issuer);
        Assert.Equal("Audience.Teste", settings.Audience);
        Assert.True(settings.SigningKey.KeySize >= 256);
    }
}
