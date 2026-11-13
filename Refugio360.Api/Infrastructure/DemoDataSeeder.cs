using Microsoft.EntityFrameworkCore;

namespace Refugio360.Api;

internal static class DemoDataSeeder
{
    private static readonly Guid FirstLocationId = Guid.Parse("489e3c91-b5d4-45c4-aea0-861ea950b614");
    private static readonly Guid SecondLocationId = Guid.Parse("73d54e8d-cc99-4bbb-a5b9-bf99b81a458e");
    private static readonly Guid AlertId = Guid.Parse("17b9e9fb-7725-4e37-b38d-e66808e952b8");

    public static async Task SeedAsync(RefugioDbContext db, string? demoUsername)
    {
        if (!await db.SafeLocations.AnyAsync(x => x.Id == FirstLocationId))
        {
            db.SafeLocations.Add(new SafeLocation
            {
                Id = FirstLocationId,
                Nome = "DEMO - Centro de apoio fictício",
                Endereco = "Endereço fictício para apresentação, São Paulo - SP",
                Latitude = -23.5505,
                Longitude = -46.6333,
                Acessivel = true,
                Disponibilidade = "Simulação: disponibilidade não confirmada",
                Ativo = true
            });
        }

        if (!await db.SafeLocations.AnyAsync(x => x.Id == SecondLocationId))
        {
            db.SafeLocations.Add(new SafeLocation
            {
                Id = SecondLocationId,
                Nome = "DEMO - Ponto de acolhimento fictício",
                Endereco = "Outro endereço fictício para apresentação, São Paulo - SP",
                Latitude = -23.5614,
                Longitude = -46.6565,
                Acessivel = false,
                Disponibilidade = "Simulação: disponibilidade não confirmada",
                Ativo = true
            });
        }

        if (!await db.Alerts.AnyAsync(x => x.Id == AlertId))
        {
            var now = DateTime.UtcNow;
            db.Alerts.Add(new Alert
            {
                Id = AlertId,
                Titulo = "DEMO - Alerta de chuva simulada",
                Mensagem = "Alerta fictício para demonstrar a API. Não representa uma ocorrência real.",
                Fonte = "Simulação acadêmica - dados fictícios",
                Status = AlertStatuses.Publicado,
                CriadoEmUtc = now,
                ExpiraEmUtc = now.AddYears(1),
                Ativo = true
            });
        }

        if (!string.IsNullOrWhiteSpace(demoUsername) &&
            !await db.Occurrences.AnyAsync(x => x.CriadaPor == demoUsername))
        {
            db.Occurrences.Add(new Occurrence
            {
                Id = Guid.NewGuid(),
                Categoria = "Simulação de enchente",
                Descricao = "Relato fictício de alagamento para apresentação do projeto.",
                Latitude = -23.5489,
                Longitude = -46.6388,
                Status = OccurrenceStatuses.Recebida,
                CriadaEmUtc = DateTime.UtcNow,
                CriadaPor = demoUsername
            });
        }

        await db.SaveChangesAsync();
    }
}
