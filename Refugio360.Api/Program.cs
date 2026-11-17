using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Refugio360.Api;

var builder = WebApplication.CreateBuilder(args);

var jwtSecret = builder.Configuration["Jwt:Secret"] ?? Environment.GetEnvironmentVariable("REFUGIO_JWT_SECRET");
if (string.IsNullOrWhiteSpace(jwtSecret) &&
    (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
{
    jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    if (builder.Environment.IsDevelopment())
        Console.WriteLine("JWT development key generated for this run. Configure User Secrets for stable tokens.");
}
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException("Configure Jwt:Secret (at least 32 UTF-8 bytes) using user secrets or REFUGIO_JWT_SECRET.");

var issuer = builder.Configuration["Jwt:Issuer"] ?? "Refugio360";
var audience = builder.Configuration["Jwt:Audience"] ?? "Refugio360.Mobile";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddDbContext<RefugioDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Refugio") ?? "Data Source=refugio360.db"));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Refúgio 360 API", Version = "v1" });
    options.OperationFilter<AuthorizeOperationFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        In = ParameterLocation.Header, Description = "Informe o token JWT retornado por POST /api/v1/auth/token."
    });
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = issuer,
        ValidateAudience = true, ValidAudience = audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey,
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = builder.Configuration.GetValue("RateLimit:PermitLimit", 60),
        Window = TimeSpan.FromMinutes(builder.Configuration.GetValue("RateLimit:WindowMinutes", 1)),
        QueueLimit = 0,
        AutoReplenishment = true
    }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<RefugioDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SqliteSchemaUpdater.UpgradeAsync(db);
    if (app.Environment.IsDevelopment())
    {
        var demoUsername = app.Configuration["DemoUser:Username"] ?? Environment.GetEnvironmentVariable("REFUGIO_DEMO_USER");
        await DemoDataSeeder.SeedAsync(db, demoUsername);
    }
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() })
        });
    }
});

var api = app.MapGroup("/api/v1").RequireRateLimiting("api");

api.MapPost("/auth/token", (TokenRequest request, IConfiguration config) =>
{
    var expectedUser = config["DemoUser:Username"] ?? Environment.GetEnvironmentVariable("REFUGIO_DEMO_USER");
    var expectedPassword = config["DemoUser:Password"] ?? Environment.GetEnvironmentVariable("REFUGIO_DEMO_PASSWORD");
    if (string.IsNullOrWhiteSpace(expectedUser) || string.IsNullOrWhiteSpace(expectedPassword))
        return Results.Problem("Configure REFUGIO_DEMO_USER e REFUGIO_DEMO_PASSWORD para habilitar o login de demonstração.", statusCode: 503);

    if (!FixedTimeEquals(request.Username, expectedUser) || !FixedTimeEquals(request.Password, expectedPassword))
        return Results.Unauthorized();

    var now = DateTime.UtcNow;
    var role = config["DemoUser:Role"] ?? Environment.GetEnvironmentVariable("REFUGIO_DEMO_ROLE") ?? "Morador";
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, expectedUser),
        new Claim(ClaimTypes.Name, expectedUser),
        new Claim(ClaimTypes.Role, role)
    };
    var token = new JwtSecurityToken(config["Jwt:Issuer"] ?? "Refugio360", config["Jwt:Audience"] ?? "Refugio360.Mobile",
        claims, now, now.AddHours(2), new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new TokenResponse(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", 7200));
}).AllowAnonymous().WithName("CreateToken").WithSummary("Emite um token JWT de demonstração.")
  .Produces<TokenResponse>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

api.MapGet("/locais-seguros", async (RefugioDbContext db, bool? acessivel, CancellationToken ct) =>
    Results.Ok(await db.SafeLocations.AsNoTracking()
        .Where(item => item.Ativo && (!acessivel.HasValue || item.Acessivel == acessivel.Value))
        .OrderBy(item => item.Nome).ToListAsync(ct)))
    .AllowAnonymous().WithName("ListSafeLocations").Produces<List<SafeLocation>>(StatusCodes.Status200OK);

api.MapGet("/locais-seguros/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.SafeLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).AllowAnonymous().WithName("GetSafeLocation").Produces<SafeLocation>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

api.MapPost("/ocorrencias", async (HttpContext http, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    CreateOccurrenceRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateOccurrence(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var key = idempotencyKey ?? "";
    if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        return Results.Problem("Envie um Idempotency-Key com até 100 caracteres.", statusCode: 400);

    var owner = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.User.Identity?.Name ?? "unknown";
    var existing = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Owner == owner && x.Key == key, ct);
    if (existing is not null)
        return Results.Content(existing.ResponseJson, "application/json", Encoding.UTF8, existing.StatusCode);

    var occurrence = new Occurrence
    {
        Id = Guid.NewGuid(), Categoria = request.Categoria.Trim(), Descricao = request.Descricao.Trim(),
        Latitude = request.Latitude, Longitude = request.Longitude, Status = OccurrenceStatuses.Recebida, CriadaEmUtc = DateTime.UtcNow,
        CriadaPor = owner
    };
    var response = JsonSerializer.Serialize(occurrence, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    db.Occurrences.Add(occurrence);
    db.IdempotencyRecords.Add(new IdempotencyRecord { Owner = owner, Key = key, ResponseJson = response, StatusCode = 201, CriadoEmUtc = DateTime.UtcNow });
    try
    {
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
        await transaction.RollbackAsync(ct);
        var duplicate = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Owner == owner && x.Key == key, ct);
        if (duplicate is not null) return Results.Content(duplicate.ResponseJson, "application/json", Encoding.UTF8, duplicate.StatusCode);
        throw;
    }
    return Results.Created($"/api/v1/ocorrencias/{occurrence.Id}", occurrence);
}).RequireAuthorization().WithName("CreateOccurrence")
  .Produces<Occurrence>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status429TooManyRequests);

api.MapGet("/ocorrencias", async (HttpContext http, RefugioDbContext db, CancellationToken ct) =>
{
    var owner = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.User.Identity?.Name;
    return Results.Ok(await db.Occurrences.AsNoTracking().Where(x => x.CriadaPor == owner && x.Status != OccurrenceStatuses.Excluida)
        .OrderByDescending(x => x.CriadaEmUtc).ToListAsync(ct));
}).RequireAuthorization().WithName("ListMyOccurrences").Produces<List<Occurrence>>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized);

api.MapGet("/ocorrencias/{id:guid}", async (Guid id, HttpContext http, RefugioDbContext db, CancellationToken ct) =>
{
    var owner = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.User.Identity?.Name;
    var item = await db.Occurrences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CriadaPor == owner && x.Status != OccurrenceStatuses.Excluida, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequireAuthorization().WithName("GetMyOccurrence").Produces<Occurrence>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status404NotFound);

api.MapPut("/ocorrencias/{id:guid}", async (Guid id, HttpContext http, UpdateOccurrenceRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateOccurrence(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var owner = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.User.Identity?.Name;
    var item = await db.Occurrences.FirstOrDefaultAsync(x => x.Id == id && x.CriadaPor == owner && x.Status != OccurrenceStatuses.Excluida, ct);
    if (item is null) return Results.NotFound();
    if (item.Status != OccurrenceStatuses.Recebida)
        return Results.Problem("A ocorrência só pode ser corrigida enquanto estiver Recebida.", statusCode: 409);
    item.Categoria = request.Categoria.Trim();
    item.Descricao = request.Descricao.Trim();
    item.Latitude = request.Latitude;
    item.Longitude = request.Longitude;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization().WithName("UpdateMyOccurrence")
  .Produces<Occurrence>(StatusCodes.Status200OK).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

api.MapDelete("/ocorrencias/{id:guid}", async (Guid id, HttpContext http, RefugioDbContext db, CancellationToken ct) =>
{
    var owner = http.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? http.User.Identity?.Name;
    var item = await db.Occurrences.FirstOrDefaultAsync(x => x.Id == id && x.CriadaPor == owner && x.Status != OccurrenceStatuses.Excluida, ct);
    if (item is null) return Results.NotFound();
    if (item.Status != OccurrenceStatuses.Recebida)
        return Results.Problem("A ocorrência só pode ser excluída enquanto estiver Recebida.", statusCode: 409);
    item.Status = OccurrenceStatuses.Excluida;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization().WithName("DeleteMyOccurrence")
  .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

api.MapGet("/triagem/ocorrencias", async (RefugioDbContext db, string? status, CancellationToken ct) =>
{
    var selectedStatus = status ?? OccurrenceStatuses.Recebida;
    if (!TriageRules.IsVisibleStatus(selectedStatus))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use Recebida, EmAnalise, Confirmada ou Descartada."] });
    return Results.Ok(await db.Occurrences.AsNoTracking().Where(x => x.Status == selectedStatus)
        .OrderBy(x => x.CriadaEmUtc).ToListAsync(ct));
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("ListOccurrencesForTriage")
  .WithSummary("Lista ocorrências por status; o padrão é Recebida.")
  .Produces<List<Occurrence>>(StatusCodes.Status200OK).ProducesValidationProblem()
  .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);

api.MapGet("/triagem/ocorrencias/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.Occurrences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status != OccurrenceStatuses.Excluida, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("GetOccurrenceForTriage")
  .Produces<Occurrence>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

api.MapPatch("/triagem/ocorrencias/{id:guid}/status", async (Guid id, UpdateOccurrenceStatusRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    if (!TriageRules.IsVisibleStatus(request.Status))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use Recebida, EmAnalise, Confirmada ou Descartada."] });
    var item = await db.Occurrences.FirstOrDefaultAsync(x => x.Id == id && x.Status != OccurrenceStatuses.Excluida, ct);
    if (item is null) return Results.NotFound();
    if (!TriageRules.CanChangeStatus(item.Status, request.Status))
        return Results.Problem("Transição inválida: Recebida -> EmAnalise -> Confirmada ou Descartada.", statusCode: 409);
    item.Status = request.Status;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("ChangeOccurrenceStatus")
  .WithSummary("Avança a triagem: Recebida, EmAnalise, Confirmada ou Descartada.")
  .Produces<Occurrence>(StatusCodes.Status200OK).ProducesValidationProblem()
  .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
  .Produces(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

api.MapGet("/alertas", async (RefugioDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Alerts.AsNoTracking().Where(x => x.Ativo && x.Status == AlertStatuses.Publicado && x.ExpiraEmUtc > DateTime.UtcNow)
        .OrderByDescending(x => x.CriadoEmUtc).ToListAsync(ct)))
    .AllowAnonymous().WithName("ListActiveAlerts").Produces<List<Alert>>(StatusCodes.Status200OK);

api.MapGet("/alertas/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo && x.Status == AlertStatuses.Publicado && x.ExpiraEmUtc > DateTime.UtcNow, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).AllowAnonymous().WithName("GetActiveAlert").Produces<Alert>(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

api.MapPost("/locais-seguros", async (CreateSafeLocationRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateSafeLocation(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var item = new SafeLocation
    {
        Id = Guid.NewGuid(), Nome = request.Nome.Trim(), Endereco = request.Endereco.Trim(),
        Latitude = request.Latitude, Longitude = request.Longitude, Acessivel = request.Acessivel,
        Disponibilidade = request.Disponibilidade.Trim(), DisponibilidadeVerificadaEmUtc = null, Ativo = true
    };
    db.SafeLocations.Add(item);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/locais-seguros/{item.Id}", item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("CreateSafeLocation")
  .Produces<SafeLocation>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);

api.MapPut("/locais-seguros/{id:guid}", async (Guid id, UpdateSafeLocationRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateSafeLocation(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var item = await db.SafeLocations.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    item.Nome = request.Nome.Trim();
    item.Endereco = request.Endereco.Trim();
    item.Latitude = request.Latitude;
    item.Longitude = request.Longitude;
    item.Acessivel = request.Acessivel;
    var updatedAvailability = request.Disponibilidade.Trim();
    if (item.Disponibilidade != updatedAvailability)
        item.DisponibilidadeVerificadaEmUtc = null;
    item.Disponibilidade = updatedAvailability;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("UpdateSafeLocation")
  .Produces<SafeLocation>(StatusCodes.Status200OK).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

api.MapPatch("/locais-seguros/{id:guid}/disponibilidade", async (Guid id, ConfirmAvailabilityRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateAvailability(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var item = await db.SafeLocations.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    item.Disponibilidade = request.Disponibilidade.Trim();
    item.DisponibilidadeVerificadaEmUtc = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("ConfirmSafeLocationAvailability")
  .WithSummary("Confirma a disponibilidade e registra a data UTC da verificação.")
  .Produces<SafeLocation>(StatusCodes.Status200OK).ProducesValidationProblem()
  .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
  .Produces(StatusCodes.Status404NotFound);

api.MapDelete("/locais-seguros/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.SafeLocations.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    item.Ativo = false;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("DeleteSafeLocation")
  .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

api.MapPost("/alertas", async (CreateAlertRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateAlert(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var item = new Alert
    {
        Id = Guid.NewGuid(), Titulo = request.Titulo.Trim(), Mensagem = request.Mensagem.Trim(),
        Fonte = request.Fonte.Trim(), Status = AlertStatuses.EmRevisao, CriadoEmUtc = DateTime.UtcNow,
        ExpiraEmUtc = request.ExpiraEmUtc, Ativo = true
    };
    db.Alerts.Add(item);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/v1/triagem/alertas/{item.Id}", item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("CreateAlert")
  .WithSummary("Cria um alerta em revisão, ainda invisível ao público.")
  .Produces<Alert>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);

api.MapGet("/triagem/alertas", async (RefugioDbContext db, string? status, CancellationToken ct) =>
{
    var selectedStatus = status ?? AlertStatuses.EmRevisao;
    if (selectedStatus is not (AlertStatuses.EmRevisao or AlertStatuses.Publicado))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use EmRevisao ou Publicado."] });
    return Results.Ok(await db.Alerts.AsNoTracking().Where(x => x.Ativo && x.Status == selectedStatus)
        .OrderByDescending(x => x.CriadoEmUtc).ToListAsync(ct));
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("ListAlertsForReview")
  .Produces<List<Alert>>(StatusCodes.Status200OK).ProducesValidationProblem()
  .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden);

api.MapGet("/triagem/alertas/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("GetAlertForReview")
  .Produces<Alert>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

api.MapPatch("/triagem/alertas/{id:guid}/publicar", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    if (item.ExpiraEmUtc <= DateTime.UtcNow)
        return Results.Problem("Atualize a validade do alerta antes de publicá-lo.", statusCode: 409);
    item.Status = AlertStatuses.Publicado;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("PublishReviewedAlert")
  .WithSummary("Publica um alerta após revisão manual pelo moderador.")
  .Produces<Alert>(StatusCodes.Status200OK).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound)
  .ProducesProblem(StatusCodes.Status409Conflict);

api.MapPut("/alertas/{id:guid}", async (Guid id, UpdateAlertRequest request, RefugioDbContext db, CancellationToken ct) =>
{
    var errors = RequestValidator.ValidateAlert(request);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var item = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    item.Titulo = request.Titulo.Trim();
    item.Mensagem = request.Mensagem.Trim();
    item.Fonte = request.Fonte.Trim();
    item.ExpiraEmUtc = request.ExpiraEmUtc;
    item.Status = AlertStatuses.EmRevisao;
    await db.SaveChangesAsync(ct);
    return Results.Ok(item);
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("UpdateAlert")
  .Produces<Alert>(StatusCodes.Status200OK).ProducesValidationProblem().Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

api.MapDelete("/alertas/{id:guid}", async (Guid id, RefugioDbContext db, CancellationToken ct) =>
{
    var item = await db.Alerts.FirstOrDefaultAsync(x => x.Id == id && x.Ativo, ct);
    if (item is null) return Results.NotFound();
    item.Ativo = false;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequireAuthorization(policy => policy.RequireRole("Moderador")).WithName("DeleteAlert")
  .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status401Unauthorized)
  .Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound);

app.Run();

static bool FixedTimeEquals(string actual, string expected)
{
    var left = Encoding.UTF8.GetBytes(actual ?? "");
    var right = Encoding.UTF8.GetBytes(expected);
    return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}

public partial class Program { }
