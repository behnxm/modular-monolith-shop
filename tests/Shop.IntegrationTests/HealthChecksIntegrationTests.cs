using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shop.BuildingBlocks.Testing;
using Shop.Host;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.IntegrationTests;

[Collection("HostCollection")]
public sealed class HealthChecksIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public HealthChecksIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // 1. Run migrations for all modules
        await MigrateAsync<CatalogDbContext>("catalog");
        await MigrateAsync<InventoryDbContext>("inventory");
        await MigrateAsync<OrderingDbContext>("ordering");
        await MigrateAsync<PaymentsDbContext>("payments");
        await MigrateAsync<NotificationsDbContext>("notifications");

        // 2. Setup WebApplicationFactory
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:catalog"] = _fixture.GetAppConnectionString("catalog"),
                    ["ConnectionStrings:inventory"] = _fixture.GetAppConnectionString("inventory"),
                    ["ConnectionStrings:ordering"] = _fixture.GetAppConnectionString("ordering"),
                    ["ConnectionStrings:payments"] = _fixture.GetAppConnectionString("payments"),
                    ["ConnectionStrings:notifications"] = _fixture.GetAppConnectionString("notifications"),
                    ["DevIssuer:Enabled"] = "true",
                    ["DevIssuer:SigningKeyPath"] = "secrets/test-dev-issuer.key"
                });
            });
        });

        _client = _factory.CreateClient();
    }

    private async Task MigrateAsync<TContext>(string schema) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

        await using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        await PostgreSqlFixture.MigrateContextAsync(context);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task LiveEndpoint_ReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/health/live", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(ct);
        content.ShouldBe("Healthy");
    }

    [Fact]
    public async Task ReadyEndpoint_ReturnsOkWhenDatabasesConnected()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/health/ready", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync(ct);
        content.ShouldBe("Healthy");
    }

    [Fact]
    public async Task ModulesEndpoint_ReturnsJsonWithModuleOutboxStatuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/health/modules", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().ShouldBe("Healthy");
        var entries = root.GetProperty("entries");

        entries.TryGetProperty("outbox_catalog", out _).ShouldBeTrue();
        entries.TryGetProperty("outbox_inventory", out _).ShouldBeTrue();
        entries.TryGetProperty("outbox_ordering", out _).ShouldBeTrue();
        entries.TryGetProperty("outbox_payments", out _).ShouldBeTrue();
        entries.TryGetProperty("outbox_notifications", out _).ShouldBeTrue();
    }
}
