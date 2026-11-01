using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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
public sealed class AuthorizationMatrixTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _adminToken = null!;
    private string _customerToken = null!;
    private string _otherCustomerToken = null!;

    public AuthorizationMatrixTests(PostgreSqlFixture fixture)
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
                    ["DevIssuer:SigningKeyPath"] = "secrets/test-auth-matrix.key"
                });
            });
        });

        _client = _factory.CreateClient();

        // Obtain dev tokens
        _adminToken = await RequestDevTokenAsync("admin_user", ["shop.admin", "shop.customer"]);
        _customerToken = await RequestDevTokenAsync("cust_alice", ["shop.customer"]);
        _otherCustomerToken = await RequestDevTokenAsync("cust_bob", ["shop.customer"]);
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

    private async Task<string> RequestDevTokenAsync(string userId, string[] scopes)
    {
        var response = await _client.PostAsJsonAsync("/dev/token", new
        {
            userId,
            displayName = userId,
            scopes
        }, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string? token = null)
    {
        var req = new HttpRequestMessage(method, uri);
        if (!string.IsNullOrEmpty(token))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return req;
    }

    [Fact]
    public async Task Anonymous_AccessingProtectedEndpoints_Returns401Unauthorized()
    {
        var ct = TestContext.Current.CancellationToken;
        var protectedEndpoints = new[]
        {
            (HttpMethod.Get, "/api/v1/catalog/products"),
            (HttpMethod.Get, $"/api/v1/catalog/products/{Guid.NewGuid()}"),
            (HttpMethod.Post, "/api/v1/catalog/products"),
            (HttpMethod.Put, $"/api/v1/catalog/products/{Guid.NewGuid()}"),
            (HttpMethod.Delete, $"/api/v1/catalog/products/{Guid.NewGuid()}"),
            (HttpMethod.Put, $"/api/v1/inventory/stock/{Guid.NewGuid()}"),
            (HttpMethod.Get, $"/api/v1/inventory/stock/{Guid.NewGuid()}"),
            (HttpMethod.Get, "/api/v1/inventory/stock"),
            (HttpMethod.Post, "/api/v1/ordering/orders"),
            (HttpMethod.Get, $"/api/v1/ordering/orders/{Guid.NewGuid()}"),
            (HttpMethod.Get, "/api/v1/ordering/orders"),
            (HttpMethod.Get, "/api/v1/ordering/orders/admin"),
            (HttpMethod.Post, $"/api/v1/ordering/orders/{Guid.NewGuid()}/cancel"),
            (HttpMethod.Get, $"/api/v1/payments/{Guid.NewGuid()}"),
            (HttpMethod.Get, "/api/v1/notifications")
        };

        foreach (var (method, uri) in protectedEndpoints)
        {
            using var req = CreateRequest(method, uri, token: null);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
            {
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var resp = await _client.SendAsync(req, ct);
            resp.StatusCode.ShouldBe(
                HttpStatusCode.Unauthorized,
                $"Endpoint '{method} {uri}' should return 401 Unauthorized for anonymous requests.");
        }
    }

    [Fact]
    public async Task Anonymous_AccessingPublicEndpoints_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;

        using var healthLive = await _client.GetAsync("/health/live", ct);
        healthLive.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var healthReady = await _client.GetAsync("/health/ready", ct);
        healthReady.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var healthModules = await _client.GetAsync("/health/modules", ct);
        healthModules.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var openApi = await _client.GetAsync("/openapi/v1.json", ct);
        openApi.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Customer_AccessingAdminEndpoints_Returns403Forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var adminEndpoints = new[]
        {
            (HttpMethod.Post, "/api/v1/catalog/products"),
            (HttpMethod.Put, $"/api/v1/catalog/products/{Guid.NewGuid()}"),
            (HttpMethod.Delete, $"/api/v1/catalog/products/{Guid.NewGuid()}"),
            (HttpMethod.Put, $"/api/v1/inventory/stock/{Guid.NewGuid()}"),
            (HttpMethod.Get, "/api/v1/ordering/orders/admin"),
            (HttpMethod.Get, $"/api/v1/payments/{Guid.NewGuid()}")
        };

        foreach (var (method, uri) in adminEndpoints)
        {
            using var req = CreateRequest(method, uri, token: _customerToken);
            if (method == HttpMethod.Post || method == HttpMethod.Put)
            {
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            }

            using var resp = await _client.SendAsync(req, ct);
            resp.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                $"Endpoint '{method} {uri}' should return 403 Forbidden for customer token lacking shop.admin.");
        }
    }

    [Fact]
    public async Task Customer_AccessingCustomerEndpoints_Succeeds()
    {
        var ct = TestContext.Current.CancellationToken;

        using var myOrdersReq = CreateRequest(HttpMethod.Get, "/api/v1/ordering/orders", token: _customerToken);
        using var myOrdersResp = await _client.SendAsync(myOrdersReq, ct);
        myOrdersResp.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var notificationsReq = CreateRequest(HttpMethod.Get, "/api/v1/notifications", token: _customerToken);
        using var notificationsResp = await _client.SendAsync(notificationsReq, ct);
        notificationsResp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StrictJson_DisallowingUnmappedMembers_Returns400BadRequest()
    {
        var ct = TestContext.Current.CancellationToken;

        // Post invalid/unmapped property to catalog endpoint as admin
        using var req = CreateRequest(HttpMethod.Post, "/api/v1/catalog/products", token: _adminToken);
        req.Content = new StringContent(
            """
            {
                "sku": "VALID-SKU-01",
                "name": "Valid Name",
                "description": "Valid Description",
                "price": 49.99,
                "unexpectedField": "this_should_cause_validation_failure"
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var resp = await _client.SendAsync(req, ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync(ct);
        json.ShouldContain("unexpectedField");
    }

    [Fact]
    public async Task RequestSizeLimit_PayloadOver64Kb_Returns413Or400()
    {
        var ct = TestContext.Current.CancellationToken;

        using var req = CreateRequest(HttpMethod.Post, "/api/v1/catalog/products", token: _adminToken);
        // Create 70 KB payload
        var largeString = new string('x', 70 * 1024);
        req.Content = new StringContent(
            $$"""
            {
                "sku": "LARGE-SKU",
                "name": "Large Product",
                "description": "{{largeString}}",
                "price": 10.00
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var resp = await _client.SendAsync(req, ct);
        // Kestrel rejects over-limit body with 413 Payload Too Large or 400 Bad Request
        var isRejected = resp.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest;
        isRejected.ShouldBeTrue($"Expected 413 or 400 for payload > 64KB, but got {resp.StatusCode}");
    }

    [Fact]
    public async Task RateLimiting_ExceedingDevTokenRateLimit_Returns429WithRetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        var hit429 = false;

        // Dev-token policy permits 10 requests per minute
        for (var i = 0; i < 15; i++)
        {
            using var resp = await _client.PostAsJsonAsync("/dev/token", new
            {
                userId = $"rate_limit_user_{i}",
                displayName = "Rate Limit Test",
                scopes = new[] { "shop.customer" }
            }, ct);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                hit429 = true;
                resp.Headers.TryGetValues("Retry-After", out var retryAfter).ShouldBeTrue();
                retryAfter.ShouldNotBeNull();
                break;
            }
        }

        hit429.ShouldBeTrue("Should receive 429 Too Many Requests after exceeding rate limit.");
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnAllResponses()
    {
        var ct = TestContext.Current.CancellationToken;

        using var resp = await _client.GetAsync("/health/live", ct);
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);

        resp.Headers.TryGetValues("X-Content-Type-Options", out var contentTypeOptions).ShouldBeTrue();
        contentTypeOptions.First().ShouldBe("nosniff");

        resp.Headers.TryGetValues("X-Frame-Options", out var frameOptions).ShouldBeTrue();
        frameOptions.First().ShouldBe("DENY");

        resp.Headers.TryGetValues("Referrer-Policy", out var referrerPolicy).ShouldBeTrue();
        referrerPolicy.First().ShouldBe("no-referrer");

        resp.Headers.TryGetValues("Content-Security-Policy", out var csp).ShouldBeTrue();
        csp.First().ShouldContain("default-src 'none'");

        resp.Headers.Contains("Server").ShouldBeFalse();
    }
}
