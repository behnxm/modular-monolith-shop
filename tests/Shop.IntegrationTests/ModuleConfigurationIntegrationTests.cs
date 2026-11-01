using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Testing;
using Shop.Host;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.IntegrationTests;

[Collection("HostCollection")]
public sealed class ModuleConfigurationIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public ModuleConfigurationIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await MigrateAsync<CatalogDbContext>("catalog");
        await MigrateAsync<InventoryDbContext>("inventory");
        await MigrateAsync<OrderingDbContext>("ordering");
        await MigrateAsync<PaymentsDbContext>("payments");
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public void Validation_ThrowsInvalidOperationException_WhenRequiredModuleMissing()
    {
        // Ordering requires catalog, inventory, payments. If only ordering is present, validation must fail fast.
        var ex = Should.Throw<InvalidOperationException>(() =>
        {
            ModuleValidator.ValidateDependencies([new OrderingModule()]);
        });

        ex.Message.ShouldContain("ordering' requires module 'catalog'");
    }

    [Fact]
    public async Task Startup_Succeeds_WhenIndependentModuleIsDisabled()
    {
        try
        {
            Environment.SetEnvironmentVariable("Modules__Enabled__0", "catalog");
            Environment.SetEnvironmentVariable("Modules__Enabled__1", "inventory");
            Environment.SetEnvironmentVariable("Modules__Enabled__2", "ordering");
            Environment.SetEnvironmentVariable("Modules__Enabled__3", "payments");
            Environment.SetEnvironmentVariable("Modules__Enabled__4", null);

            // Run without notifications module
            await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                        ["DevIssuer:Enabled"] = "true",
                        ["DevIssuer:SigningKeyPath"] = "secrets/test-dev-issuer.key"
                    });
                });
            });

            using var client = factory.CreateClient();
            var ct = TestContext.Current.CancellationToken;

            // Obtain token from /dev/token with shop.customer scope
            var tokenResponse = await client.PostAsJsonAsync("/dev/token", new DevTokenRequest("cust_01", "Customer 1", ["shop.customer"]), ct);
            tokenResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct);
            var token = tokenJson.GetProperty("token").GetString()!;

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Catalog endpoints work (200 OK)
            var catalogResponse = await client.GetAsync("/api/v1/catalog/products/", ct);
            var catalogContent = await catalogResponse.Content.ReadAsStringAsync(ct);
            catalogResponse.StatusCode.ShouldBe(HttpStatusCode.OK, catalogContent);

            // Notifications endpoint is not mapped (404 Not Found)
            var notifResponse = await client.GetAsync("/api/v1/notifications", ct);
            notifResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Modules__Enabled__0", null);
            Environment.SetEnvironmentVariable("Modules__Enabled__1", null);
            Environment.SetEnvironmentVariable("Modules__Enabled__2", null);
            Environment.SetEnvironmentVariable("Modules__Enabled__3", null);
        }
    }
}
