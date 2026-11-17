using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Refugio360.Api;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class ApiFlowTests : IClassFixture<RefugioApiFactory>
{
    private readonly HttpClient _client;
    private readonly RefugioApiFactory _factory;

    public ApiFlowTests(RefugioApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Protected_endpoint_rejects_request_without_token()
    {
        var response = await _client.GetAsync("/api/v1/ocorrencias");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_rejects_invalid_token()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
        var response = await _client.GetAsync("/api/v1/ocorrencias");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Morador_token_cannot_publish_moderator_alert()
    {
        await AuthenticateAsDemoUser();
        var response = await _client.PostAsJsonAsync("/api/v1/alertas", new
        {
            titulo = "Alerta de teste", mensagem = "Conteúdo fictício", fonte = "Fonte de demonstração",
            expiraEmUtc = DateTime.UtcNow.AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_and_health_endpoints_are_available()
    {
        var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, swaggerResponse.StatusCode);
        using var swagger = JsonDocument.Parse(await swaggerResponse.Content.ReadAsStringAsync());
        var occurrencePost = swagger.RootElement.GetProperty("paths").GetProperty("/api/v1/ocorrencias").GetProperty("post");
        Assert.True(occurrencePost.GetProperty("parameters").EnumerateArray().Any(parameter =>
            parameter.GetProperty("name").GetString() == "Idempotency-Key" &&
            parameter.GetProperty("in").GetString() == "header"));
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Morador_can_create_occurrence_and_repeated_key_does_not_duplicate_it()
    {
        await AuthenticateAsDemoUser();
        var payload = new { categoria = "Enchente", descricao = "Água acumulada na rua de teste", latitude = -23.55, longitude = -46.63 };
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ocorrencias") { Content = JsonContent.Create(payload) };
        firstRequest.Headers.Add("Idempotency-Key", "teste-duplicidade-001");
        var first = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var id = firstJson.RootElement.GetProperty("id").GetString();

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ocorrencias") { Content = JsonContent.Create(payload) };
        secondRequest.Headers.Add("Idempotency-Key", "teste-duplicidade-001");
        var second = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(id, secondJson.RootElement.GetProperty("id").GetString());

        using var listResponse = await _client.GetAsync("/api/v1/ocorrencias");
        listResponse.EnsureSuccessStatusCode();
        using var listJson = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, listJson.RootElement.GetArrayLength());
    }

    private async Task AuthenticateAsDemoUser()
    {
        var tokenResponse = await _client.PostAsJsonAsync("/api/v1/auth/token", new { username = _factory.Username, password = _factory.Password });
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokenJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}

public sealed class RefugioApiFactory : WebApplicationFactory<global::Program>
{
    private readonly string _connectionString = $"Data Source=refugio-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _anchor;
    public string Username { get; } = "teste";
    public string Password { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
    public string Role { get; set; } = "Morador";
    private string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public RefugioApiFactory()
    {
        _anchor = new SqliteConnection(_connectionString);
        _anchor.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = JwtSecret
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = JwtSecret,
            ["DemoUser:Username"] = Username,
            ["DemoUser:Password"] = Password,
            ["DemoUser:Role"] = Role,
            ["ConnectionStrings:Refugio"] = _connectionString,
            ["RateLimit:PermitLimit"] = "1000"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _anchor.Dispose();
    }
}

public sealed class RateLimitTests : IClassFixture<RateLimitedApiFactory>
{
    private readonly HttpClient _client;

    public RateLimitTests(RateLimitedApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Request_over_configured_limit_returns_429()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/v1/locais-seguros")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await _client.GetAsync("/api/v1/locais-seguros")).StatusCode);
    }
}

public sealed class RateLimitedApiFactory : WebApplicationFactory<global::Program>
{
    private readonly string _connectionString = $"Data Source=refugio-rate-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _anchor;
    private string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public RateLimitedApiFactory()
    {
        _anchor = new SqliteConnection(_connectionString);
        _anchor.Open();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = JwtSecret
        }));
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = JwtSecret,
            ["DemoUser:Username"] = "teste",
            ["DemoUser:Password"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            ["DemoUser:Role"] = "Morador",
            ["ConnectionStrings:Refugio"] = _connectionString,
            ["RateLimit:PermitLimit"] = "1",
            ["RateLimit:WindowMinutes"] = "10"
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _anchor.Dispose();
    }
}
