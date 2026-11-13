using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Refugio360.Api;

public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<RefugioDbContext>();
            var canConnect = await database.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Banco SQLite disponível.")
                : HealthCheckResult.Unhealthy("Não foi possível conectar ao banco.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Falha ao verificar o banco.", exception);
        }
    }
}
