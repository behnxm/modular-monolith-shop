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
public sealed class OpenApiSnapshotTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonIndentedOptions = new() { WriteIndented = true };
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public OpenApiSnapshotTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await MigrateAsync<CatalogDbContext>("catalog");
        await MigrateAsync<InventoryDbContext>("inventory");
        await MigrateAsync<OrderingDbContext>("ordering");
        await MigrateAsync<PaymentsDbContext>("payments");
        await MigrateAsync<NotificationsDbContext>("notifications");

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
                    ["DevIssuer:SigningKeyPath"] = "secrets/test-openapi.key"
                });
            });
        });

        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
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

    private static string GetRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "ModularMonolithShop.slnx")) ||
                Directory.Exists(Path.Combine(current, "docs")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }
            current = parent.FullName;
        }
        throw new InvalidOperationException("Could not find repository root directory.");
    }

    [Fact]
    public async Task OpenApiDocument_GeneratesAndMatchesCommittedSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var response = await _client.GetAsync("/openapi/v1.json", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var actualRawJson = await response.Content.ReadAsStringAsync(ct);
        using var actualDoc = JsonDocument.Parse(actualRawJson);

        // Verify fundamental OpenAPI properties
        var root = actualDoc.RootElement;
        root.GetProperty("openapi").GetString().ShouldNotBeNull();
        root.GetProperty("info").GetProperty("title").GetString().ShouldBe("Modular Monolith Shop API");
        root.GetProperty("info").GetProperty("version").GetString().ShouldBe("v1");

        // Verify key paths exist
        var paths = root.GetProperty("paths");
        paths.TryGetProperty("/api/v1/catalog/products", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/catalog/products/{id}", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/inventory/stock/{productId}", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/inventory/stock", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/ordering/orders", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/ordering/orders/admin", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/ordering/orders/{id}", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/ordering/orders/{id}/cancel", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/payments/{orderId}", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/v1/notifications", out _).ShouldBeTrue();

        // Format actual JSON nicely for comparison/snapshot
        var formattedActualJson = JsonSerializer.Serialize(
            actualDoc,
            JsonIndentedOptions);

        var repoRoot = GetRepositoryRoot();
        var openApiDir = Path.Combine(repoRoot, "docs", "openapi");
        Directory.CreateDirectory(openApiDir);
        var snapshotFile = Path.Combine(openApiDir, "shop.v1.json");

        if (!File.Exists(snapshotFile))
        {
            await File.WriteAllTextAsync(snapshotFile, formattedActualJson, ct);
        }

        var expectedJson = (await File.ReadAllTextAsync(snapshotFile, ct)).Trim().Replace("\r\n", "\n");
        var actualNormalized = formattedActualJson.Trim().Replace("\r\n", "\n");

        actualNormalized.ShouldBe(expectedJson, "Runtime OpenAPI document must match committed docs/openapi/shop.v1.json snapshot.");
    }
}
