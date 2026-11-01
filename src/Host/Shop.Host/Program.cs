using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Eventing;
using Shop.BuildingBlocks.Infrastructure.Inbox;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Telemetry;
using Shop.BuildingBlocks.Infrastructure.Web;
using Shop.Host;
using Shop.Modules.Catalog;
using Shop.Modules.Inventory;
using Shop.Modules.Notifications;
using Shop.Modules.Ordering;
using Shop.Modules.Payments;

var builder = WebApplication.CreateBuilder(args);

// 1. Strict Request Size Limits (64 KB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
    options.AddServerHeader = false;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 64 * 1024;
});

// 2. Strict JSON options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.MaxDepth = 64;
});

// 3. Explicit module configuration and validation
var allModules = new IModule[]
{
    new CatalogModule(),
    new InventoryModule(),
    new OrderingModule(),
    new PaymentsModule(),
    new NotificationsModule()
};

var enabledModuleNames = builder.Configuration.GetSection("Modules:Enabled").Get<string[]>()
    ?? ["catalog", "inventory", "ordering", "payments", "notifications"];

var enabledNamesSet = new HashSet<string>(enabledModuleNames.Select(n => n.ToLowerInvariant()));

var enabledModules = allModules
    .Where(m => enabledNamesSet.Contains(m.Name.ToLowerInvariant()))
    .ToList();

// Validate required dependencies between enabled modules
ModuleValidator.ValidateDependencies(enabledModules);

// 4. Infrastructure & Eventing setup
var eventRegistry = new IntegrationEventRegistry();
foreach (var module in enabledModules)
{
    eventRegistry.RegisterModuleContracts(module.ContractsAssembly);
}

builder.Services.AddSingleton(eventRegistry);
builder.Services.AddSingleton<IInboxDispatcher, InboxDispatcher>();
builder.Services.AddSingleton<IIntegrationEventTransport, InProcessEventTransport>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton(TimeProvider.System);

// 5. Authentication & Authorization
DevTokenIssuer.ValidateConfiguration(builder.Configuration, builder.Environment);
var devIssuerEnabled = builder.Configuration.GetValue<bool>("DevIssuer:Enabled");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.SaveToken = false;

        var tokenValidationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256, SecurityAlgorithms.RsaSsaPssSha256]
        };

        if (builder.Configuration.GetValue<bool>("DevIssuer:Enabled"))
        {
            var signingKey = DevTokenIssuer.GetOrLoadSigningKey(builder.Configuration, builder.Environment);
            tokenValidationParams.ValidIssuer = DevTokenIssuer.DevIssuerAuthority;
            tokenValidationParams.ValidAudience = builder.Configuration["Authentication:Audience"] ?? "shop-api";
            tokenValidationParams.IssuerSigningKey = signingKey;
            options.RequireHttpsMetadata = false;
        }
        else
        {
            tokenValidationParams.ValidIssuer = builder.Configuration["Authentication:Authority"];
            tokenValidationParams.ValidAudience = builder.Configuration["Authentication:Audience"] ?? "shop-api";
            options.Authority = builder.Configuration["Authentication:Authority"];
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        }

        options.TokenValidationParameters = tokenValidationParams;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("shop.customer", policy =>
        policy.RequireAssertion(ctx =>
        {
            var user = ctx.User;
            var claims = user.FindAll("scope").Concat(user.FindAll("scp"));
            return claims.Any(c => c.Value.Split(' ').Contains("shop.customer"));
        }));

    options.AddPolicy("shop.admin", policy =>
        policy.RequireAssertion(ctx =>
        {
            var user = ctx.User;
            var claims = user.FindAll("scope").Concat(user.FindAll("scp"));
            return claims.Any(c => c.Value.Split(' ').Contains("shop.admin"));
        }));
});

// 6. OpenAPI & Scalar UI
builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new()
        {
            Title = "Modular Monolith Shop API",
            Version = "v1",
            Description = "Reference modular monolith application built on .NET 10 LTS."
        };
        return Task.CompletedTask;
    });
});

// 7. Rate Limiting
builder.Services.AddShopRateLimiting();

// 8. Health Checks
var healthBuilder = builder.Services.AddHealthChecks();

// 9. OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Shop.Host"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(ShopTelemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql();

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(ShopTelemetry.ServiceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    });

// 10. CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod());
    });
}

// 11. Configure Services for all enabled modules
foreach (var module in enabledModules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var app = builder.Build();

// Check CLI switches before web pipeline starts
var cliExitCode = await CliRunner.HandleCliSwitchesAsync(args, app, enabledModules, app.Logger);
if (cliExitCode.HasValue)
{
    return cliExitCode.Value;
}

// Global Exception Handler
app.UseExceptionHandler();
app.UseStatusCodePages();

// Security Headers
app.UseShopSecurityHeaders();

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// OpenAPI Document & Scalar UI
app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Demonstration"))
{
    app.MapScalarApiReference("/scalar/v1", options =>
    {
        options.WithTitle("Modular Monolith Shop API");
        options.WithOpenApiRoutePattern("/openapi/v1.json");
    }).AllowAnonymous();
}

// Development Token Issuer
DevTokenIssuer.MapDevEndpoints(app, builder.Configuration, builder.Environment);

// Health Check Endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready") || check.Name.Contains("npgsql", StringComparison.OrdinalIgnoreCase)
}).AllowAnonymous();

app.MapHealthChecks("/health/modules", new HealthCheckOptions
{
    Predicate = check => check.Name.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    data = e.Value.Data
                })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
}).AllowAnonymous();

// Map Endpoints for all enabled modules
foreach (var module in enabledModules)
{
    module.MapEndpoints(app);
}

await app.RunAsync();
return 0;

public partial class Program;
