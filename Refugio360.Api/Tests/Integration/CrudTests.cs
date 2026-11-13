using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class CrudTests
{
    [Fact]
    public async Task Morador_can_read_update_and_delete_own_occurrence()
    {
        using var factory = new RefugioApiFactory();
        using var client = factory.CreateClient();
        await Authenticate(client, factory);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ocorrencias")
        {
            Content = JsonContent.Create(new
            {
                categoria = "Enchente", descricao = "Relato inicial", latitude = -23.55, longitude = -46.63
            })
        };
        createRequest.Headers.Add("Idempotency-Key", "crud-ocorrencia-001");
        var created = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await ReadId(created);
        var route = $"/api/v1/ocorrencias/{id}";

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
        var updated = await client.PutAsJsonAsync(route, new
        {
            categoria = "Deslizamento", descricao = "Relato corrigido", latitude = -23.56, longitude = -46.64
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using (var body = JsonDocument.Parse(await updated.Content.ReadAsStringAsync()))
            Assert.Equal("Relato corrigido", body.RootElement.GetProperty("descricao").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        var listResponse = await client.GetAsync("/api/v1/ocorrencias");
        listResponse.EnsureSuccessStatusCode();
        using var list = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(0, list.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Moderator_can_complete_crud_for_safe_locations_and_alerts()
    {
        using var factory = new RefugioApiFactory { Role = "Moderador" };
        using var client = factory.CreateClient();
        await Authenticate(client, factory);

        var createdLocation = await client.PostAsJsonAsync("/api/v1/locais-seguros", new
        {
            nome = "Ponto fictício", endereco = "Rua de teste", latitude = -23.55, longitude = -46.63,
            acessivel = true, disponibilidade = "Simulação"
        });
        Assert.Equal(HttpStatusCode.Created, createdLocation.StatusCode);
        var locationRoute = $"/api/v1/locais-seguros/{await ReadId(createdLocation)}";
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(locationRoute)).StatusCode);
        var updatedLocation = await client.PutAsJsonAsync(locationRoute, new
        {
            nome = "Ponto fictício atualizado", endereco = "Outra rua de teste", latitude = -23.56,
            longitude = -46.64, acessivel = false, disponibilidade = "Simulação atualizada"
        });
        Assert.Equal(HttpStatusCode.OK, updatedLocation.StatusCode);
        using (var body = JsonDocument.Parse(await updatedLocation.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Ponto fictício atualizado", body.RootElement.GetProperty("nome").GetString());
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("disponibilidadeVerificadaEmUtc").ValueKind);
        }
        var confirmedAvailability = await PatchAsync(client, $"{locationRoute}/disponibilidade", new
        {
            disponibilidade = "Disponível nesta simulação"
        });
        Assert.Equal(HttpStatusCode.OK, confirmedAvailability.StatusCode);
        using (var body = JsonDocument.Parse(await confirmedAvailability.Content.ReadAsStringAsync()))
            Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("disponibilidadeVerificadaEmUtc").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(locationRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(locationRoute)).StatusCode);

        var createdAlert = await client.PostAsJsonAsync("/api/v1/alertas", new
        {
            titulo = "Aviso simulado", mensagem = "Mensagem inicial fictícia", fonte = "Teste acadêmico",
            expiraEmUtc = DateTime.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Created, createdAlert.StatusCode);
        var alertId = await ReadId(createdAlert);
        var alertRoute = $"/api/v1/alertas/{alertId}";
        var reviewRoute = $"/api/v1/triagem/alertas/{alertId}";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(alertRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(reviewRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(client, $"{reviewRoute}/publicar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(alertRoute)).StatusCode);
        var updatedAlert = await client.PutAsJsonAsync(alertRoute, new
        {
            titulo = "Aviso atualizado", mensagem = "Mensagem corrigida fictícia", fonte = "Teste acadêmico",
            expiraEmUtc = DateTime.UtcNow.AddDays(2)
        });
        Assert.Equal(HttpStatusCode.OK, updatedAlert.StatusCode);
        using (var body = JsonDocument.Parse(await updatedAlert.Content.ReadAsStringAsync()))
        {
            Assert.Equal("Aviso atualizado", body.RootElement.GetProperty("titulo").GetString());
            Assert.Equal("EmRevisao", body.RootElement.GetProperty("status").GetString());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(alertRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(client, $"{reviewRoute}/publicar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(alertRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(alertRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(alertRoute)).StatusCode);
    }

    [Fact]
    public async Task Morador_cannot_update_or_delete_moderated_resources()
    {
        using var factory = new RefugioApiFactory();
        using var client = factory.CreateClient();
        await Authenticate(client, factory);
        var id = Guid.NewGuid();

        var locationUpdate = await client.PutAsJsonAsync($"/api/v1/locais-seguros/{id}", new
        {
            nome = "Ponto fictício", endereco = "Rua de teste", latitude = -23.55, longitude = -46.63,
            acessivel = true, disponibilidade = "Simulação"
        });
        Assert.Equal(HttpStatusCode.Forbidden, locationUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/v1/locais-seguros/{id}")).StatusCode);

        var alertUpdate = await client.PutAsJsonAsync($"/api/v1/alertas/{id}", new
        {
            titulo = "Aviso simulado", mensagem = "Mensagem fictícia", fonte = "Teste acadêmico",
            expiraEmUtc = DateTime.UtcNow.AddDays(1)
        });
        Assert.Equal(HttpStatusCode.Forbidden, alertUpdate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"/api/v1/alertas/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/triagem/ocorrencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/triagem/alertas")).StatusCode);
    }

    [Fact]
    public async Task Triage_preserves_occurrence_and_blocks_edits_after_analysis_starts()
    {
        using var factory = new RefugioApiFactory { Role = "Moderador" };
        using var client = factory.CreateClient();
        await Authenticate(client, factory);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ocorrencias")
        {
            Content = JsonContent.Create(new
            {
                categoria = "Enchente", descricao = "Relato para triagem", latitude = -23.55, longitude = -46.63
            })
        };
        createRequest.Headers.Add("Idempotency-Key", "triagem-001");
        var created = await client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = await ReadId(created);
        var occurrenceRoute = $"/api/v1/ocorrencias/{id}";
        var statusRoute = $"/api/v1/triagem/ocorrencias/{id}/status";

        var receivedResponse = await client.GetAsync("/api/v1/triagem/ocorrencias");
        receivedResponse.EnsureSuccessStatusCode();
        using var receivedList = JsonDocument.Parse(await receivedResponse.Content.ReadAsStringAsync());
        Assert.True(receivedList.RootElement.EnumerateArray().Any(item => item.GetProperty("id").GetGuid() == id));

        var inAnalysis = await PatchAsync(client, statusRoute, new { status = "EmAnalise" });
        Assert.Equal(HttpStatusCode.OK, inAnalysis.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(occurrenceRoute, new
        {
            categoria = "Enchente", descricao = "Tentativa tardia", latitude = -23.55, longitude = -46.63
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(occurrenceRoute)).StatusCode);

        var confirmed = await PatchAsync(client, statusRoute, new { status = "Confirmada" });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var detail = await client.GetAsync(occurrenceRoute);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var body = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("Confirmada", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await PatchAsync(client, statusRoute, new { status = "Recebida" })).StatusCode);
    }

    private static async Task Authenticate(HttpClient client, RefugioApiFactory factory)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/token", new
        {
            username = factory.Username,
            password = factory.Password
        });
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("accessToken").GetString());
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, route);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<Guid> ReadId(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return Guid.Parse(body.RootElement.GetProperty("id").GetString()!);
    }
}
