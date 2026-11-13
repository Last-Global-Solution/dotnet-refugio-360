using Microsoft.EntityFrameworkCore;

namespace Refugio360.Api;

public sealed record TokenRequest(string Username, string Password);
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);
public sealed record CreateOccurrenceRequest(string Categoria, string Descricao, double Latitude, double Longitude);
public sealed record UpdateOccurrenceRequest(string Categoria, string Descricao, double Latitude, double Longitude);
public sealed record UpdateOccurrenceStatusRequest(string Status);
public sealed record CreateSafeLocationRequest(string Nome, string Endereco, double Latitude, double Longitude, bool Acessivel, string Disponibilidade);
public sealed record UpdateSafeLocationRequest(string Nome, string Endereco, double Latitude, double Longitude, bool Acessivel, string Disponibilidade);
public sealed record ConfirmAvailabilityRequest(string Disponibilidade);
public sealed record CreateAlertRequest(string Titulo, string Mensagem, string Fonte, DateTime ExpiraEmUtc);
public sealed record UpdateAlertRequest(string Titulo, string Mensagem, string Fonte, DateTime ExpiraEmUtc);

public static class OccurrenceStatuses
{
    public const string Recebida = "Recebida";
    public const string EmAnalise = "EmAnalise";
    public const string Confirmada = "Confirmada";
    public const string Descartada = "Descartada";
    public const string Excluida = "Excluida";
}

public static class AlertStatuses
{
    public const string EmRevisao = "EmRevisao";
    public const string Publicado = "Publicado";
}

public sealed class Occurrence
{
    public Guid Id { get; set; }
    public string Categoria { get; set; } = "";
    public string Descricao { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Status { get; set; } = OccurrenceStatuses.Recebida;
    public DateTime CriadaEmUtc { get; set; }
    public string CriadaPor { get; set; } = "";
}

public sealed class SafeLocation
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool Acessivel { get; set; }
    public string Disponibilidade { get; set; } = "Informação não confirmada";
    public DateTime? DisponibilidadeVerificadaEmUtc { get; set; }
    public bool Ativo { get; set; }
}

public sealed class Alert
{
    public Guid Id { get; set; }
    public string Titulo { get; set; } = "";
    public string Mensagem { get; set; } = "";
    public string Fonte { get; set; } = "";
    public string Status { get; set; } = AlertStatuses.EmRevisao;
    public DateTime CriadoEmUtc { get; set; }
    public DateTime ExpiraEmUtc { get; set; }
    public bool Ativo { get; set; }
}

public sealed class IdempotencyRecord
{
    public long Id { get; set; }
    public string Owner { get; set; } = "";
    public string Key { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public int StatusCode { get; set; }
    public DateTime CriadoEmUtc { get; set; }
}

public sealed class RefugioDbContext(DbContextOptions<RefugioDbContext> options) : DbContext(options)
{
    public DbSet<Occurrence> Occurrences => Set<Occurrence>();
    public DbSet<SafeLocation> SafeLocations => Set<SafeLocation>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Occurrence>().HasKey(x => x.Id);
        modelBuilder.Entity<Occurrence>().Property(x => x.Categoria).HasMaxLength(60).IsRequired();
        modelBuilder.Entity<Occurrence>().Property(x => x.Descricao).HasMaxLength(1000).IsRequired();
        modelBuilder.Entity<Occurrence>().Property(x => x.CriadaPor).HasMaxLength(120).IsRequired();
        modelBuilder.Entity<SafeLocation>().HasKey(x => x.Id);
        modelBuilder.Entity<Alert>().HasKey(x => x.Id);
        modelBuilder.Entity<IdempotencyRecord>().HasIndex(x => new { x.Owner, x.Key }).IsUnique();
        modelBuilder.Entity<IdempotencyRecord>().Property(x => x.Key).HasMaxLength(100);
    }
}
