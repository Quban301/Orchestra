using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Orchestra.Api.Auth;
using Orchestra.Api.Endpoints;
using Orchestra.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---- Services ----
builder.Services.AddOrchestraInfrastructure(builder.Configuration);
builder.Services.AddOrchestraScheduler();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

// Authentication: API keys in the X-Api-Key header. Everything requires a valid key unless marked anonymous.
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("Auth"));
var configuredKeys = builder.Configuration.GetSection("Auth:ApiKeys").Get<List<ApiKeyEntry>>() ?? [];
if (!builder.Environment.IsDevelopment() && !configuredKeys.Any(k => k.Key.Length >= 16))
    throw new InvalidOperationException(
        "Auth:ApiKeys must contain at least one key of 16+ characters outside Development. " +
        "Set it with environment variables, e.g. Auth__ApiKeys__0__Key.");

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.Scheme, null);

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));

// Rate limiting: a token bucket per API key (or per IP for anonymous callers).
var rate = builder.Configuration.GetSection("RateLimit");
var tokenLimit = rate.GetValue("TokenLimit", 100);
var tokensPerSecond = rate.GetValue("TokensPerSecond", 50);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var partition = ctx.User.Identity?.IsAuthenticated == true
            ? $"key:{ctx.User.Identity.Name}"
            : $"ip:{ctx.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetTokenBucketLimiter(partition, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = tokenLimit,
            TokensPerPeriod = tokensPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

// CORS for the React dashboard.
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Location")));

var app = builder.Build();

// ---- Pipeline ----
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    // Dev convenience: apply migrations on startup. Create them with:
    //   dotnet ef migrations add <Name> -p src/Orchestra.Infrastructure -s src/Orchestra.Api
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrchestraDbContext>().Database.MigrateAsync();
}

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/", () => Results.Ok(new { name = "Orchestra", status = "ok" })).AllowAnonymous();
app.MapWorkflowEndpoints();
app.MapRunEndpoints();

app.Run();

// Lets integration tests use WebApplicationFactory<Program>.
public partial class Program;
