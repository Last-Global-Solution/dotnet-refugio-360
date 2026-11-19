using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Refugio360.Api;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class HostedDemoDataTests
{
    [Fact]
    public async Task Production_without_explicit_flag_has_no_demo_alerts()
    {
        using var factory = new RefugioApiFactory { EnvironmentName = "Production" };
        using var client = factory.CreateClient();

        var alerts = await client.GetFromJsonAsync<List<Alert>>("/api/v1/alertas");
        Assert.NotNull(alerts);
        Assert.Empty(alerts);
    }

    [Fact]
    public async Task Production_with_explicit_flag_publishes_demo_alert_once_and_preserves_existing_data()
    {
        using var factory = new RefugioApiFactory { EnvironmentName = "Production", EnableDemoData = true };
        using var client = factory.CreateClient();

        var first = await client.GetFromJsonAsync<List<Alert>>("/api/v1/alertas");
        var demo = Assert.Single(first!);
        Assert.StartsWith("DEMO -", demo.Titulo);
        Assert.Contains("fict", demo.Fonte, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AlertStatuses.Publicado, demo.Status);
        Assert.True(demo.Ativo);
        Assert.True(demo.ExpiraEmUtc > DateTime.UtcNow);

        var detail = await client.GetAsync($"/api/v1/alertas/{demo.Id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RefugioDbContext>();
            var existing = new Alert
            {
                Id = Guid.NewGuid(), Titulo = "Aviso real preservado", Mensagem = "Conteúdo existente",
                Fonte = "Fonte existente", Status = AlertStatuses.Publicado,
                CriadoEmUtc = DateTime.UtcNow, ExpiraEmUtc = DateTime.UtcNow.AddDays(7), Ativo = true
            };
            db.Alerts.Add(existing);
            await db.SaveChangesAsync();

            await DemoDataSeeder.SeedAsync(db, factory.Username);
            Assert.Equal(2, await db.Alerts.CountAsync());
            Assert.Equal(2, await db.SafeLocations.CountAsync());
            Assert.Equal("Aviso real preservado", (await db.Alerts.FindAsync(existing.Id))!.Titulo);
        }

        var after = await client.GetFromJsonAsync<List<Alert>>("/api/v1/alertas");
        Assert.Equal(2, after!.Count);
        Assert.Contains(after, alert => alert.Id == demo.Id && alert.Titulo == demo.Titulo && alert.ExpiraEmUtc == demo.ExpiraEmUtc);
    }
}
