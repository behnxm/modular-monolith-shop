using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog.Application.CreateProduct;
using Shop.Modules.Catalog.Application.DeactivateProduct;
using Shop.Modules.Catalog.Application.GetProduct;
using Shop.Modules.Catalog.Application.ListProducts;
using Shop.Modules.Catalog.Application.UpdateProduct;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Catalog.PublicApi;
using Shouldly;
using Xunit;

namespace Shop.Modules.Catalog.Tests;

public sealed class CatalogIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private CatalogDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero));

    public CatalogIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        // 1. Run migrations for catalog as migrator
        var migratorOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options;

        await using var migratorContext = new CatalogDbContext(migratorOptions);
        await PostgreSqlFixture.MigrateContextAsync(migratorContext);

        // 2. Connect as catalog_app for isolated tests
        var appOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("catalog"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "catalog"))
            .Options;

        _dbContext = new CatalogDbContext(appOptions);

        // 3. Reset catalog schema between test runs
        await _fixture.ResetSchemasAsync("catalog");
    }

    public async ValueTask DisposeAsync()
    {
        if (_dbContext is not null)
        {
            await _dbContext.DisposeAsync();
        }
    }

    [Fact]
    public async Task CreateProduct_ValidRequest_SavesProductAndEnqueuesOutboxEvent()
    {
        var handler = new CreateProductHandler(_dbContext, _timeProvider);
        var request = new CreateProductRequest("KEYBOARD-01", "Mechanical Keyboard", "Cherry MX Brown", 129.99m);

        var result = await handler.HandleAsync(request, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var response = result.Value;
        response.Sku.ShouldBe("KEYBOARD-01");
        response.Name.ShouldBe("Mechanical Keyboard");
        response.Price.ShouldBe(129.99m);

        // Verify product in database
        var savedProduct = await _dbContext.Products.FirstOrDefaultAsync(p => p.Id == response.Id, cancellationToken: TestContext.Current.CancellationToken);
        savedProduct.ShouldNotBeNull();
        savedProduct.Sku.Value.ShouldBe("KEYBOARD-01");

        // Verify outbox row written atomically
        var outboxMessage = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.ContractName == "catalog.product-created.v1", cancellationToken: TestContext.Current.CancellationToken);

        outboxMessage.ShouldNotBeNull();
        outboxMessage.Payload.ShouldContain("KEYBOARD-01");
        outboxMessage.ProcessedOn.ShouldBeNull();
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_ReturnsConflict409()
    {
        var handler = new CreateProductHandler(_dbContext, _timeProvider);
        var request = new CreateProductRequest("MOUSE-01", "Gaming Mouse", "16000 DPI", 79.99m);

        var firstResult = await handler.HandleAsync(request, CancellationToken.None);
        firstResult.IsSuccess.ShouldBeTrue();

        var duplicateResult = await handler.HandleAsync(request, CancellationToken.None);
        duplicateResult.IsFailure.ShouldBeTrue();
        duplicateResult.Error.Code.ShouldBe("catalog.duplicate_sku");
    }

    [Fact]
    public async Task UpdateProduct_ValidRequest_UpdatesSuccessfully()
    {
        var createHandler = new CreateProductHandler(_dbContext, _timeProvider);
        var createResult = await createHandler.HandleAsync(
            new CreateProductRequest("MONITOR-01", "4K Monitor", "IPS Panel", 399.99m),
            CancellationToken.None);

        var updateHandler = new UpdateProductHandler(_dbContext);
        var updateResult = await updateHandler.HandleAsync(
            createResult.Value.Id,
            new UpdateProductRequest("4K Gaming Monitor", "IPS 144Hz", 449.99m),
            CancellationToken.None);

        updateResult.IsSuccess.ShouldBeTrue();

        var updated = await _dbContext.Products.FindAsync(new object?[] { createResult.Value.Id }, TestContext.Current.CancellationToken);
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("4K Gaming Monitor");
        updated.Price.ShouldBe(449.99m);
        updated.Version.ShouldBe(2);
    }

    [Fact]
    public async Task UpdateProduct_NonExistent_ReturnsNotFound404()
    {
        var updateHandler = new UpdateProductHandler(_dbContext);
        var result = await updateHandler.HandleAsync(
            Guid.NewGuid(),
            new UpdateProductRequest("Ghost Product", "Ghost", 10m),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.product_not_found");
    }

    [Fact]
    public async Task DeactivateProduct_Valid_MarksProductInactive()
    {
        var createHandler = new CreateProductHandler(_dbContext, _timeProvider);
        var createResult = await createHandler.HandleAsync(
            new CreateProductRequest("HEADSET-01", "Wireless Headset", "ANC", 199.99m),
            CancellationToken.None);

        var deactivateHandler = new DeactivateProductHandler(_dbContext);
        var result = await deactivateHandler.HandleAsync(createResult.Value.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        var deactivated = await _dbContext.Products.FindAsync(new object?[] { createResult.Value.Id }, TestContext.Current.CancellationToken);
        deactivated.ShouldNotBeNull();
        deactivated.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task GetProduct_ValidAndNonExistent_ReturnsExpectedResults()
    {
        var createHandler = new CreateProductHandler(_dbContext, _timeProvider);
        var createResult = await createHandler.HandleAsync(
            new CreateProductRequest("WEBCAM-01", "1080p Webcam", "Wide angle", 69.99m),
            CancellationToken.None);

        var getHandler = new GetProductHandler(_dbContext);

        var validResult = await getHandler.HandleAsync(createResult.Value.Id, CancellationToken.None);
        validResult.IsSuccess.ShouldBeTrue();
        validResult.Value.Sku.ShouldBe("WEBCAM-01");

        var invalidResult = await getHandler.HandleAsync(Guid.NewGuid(), CancellationToken.None);
        invalidResult.IsFailure.ShouldBeTrue();
        invalidResult.Error.Code.ShouldBe("catalog.product_not_found");
    }

    [Fact]
    public async Task ListProducts_KeysetPagination_ReturnsAllItemsWithoutDuplicatesOrGaps()
    {
        // Seed 10 products with sequential timestamps
        for (var i = 1; i <= 10; i++)
        {
            var pTime = _timeProvider.GetUtcNow().AddMinutes(i);
            var prod = Product.Create($"PAG-{i:D2}", $"Item {i}", "Desc", 10m * i, new FakeTimeProvider(pTime)).Value;
            _dbContext.Products.Add(prod);
        }
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listHandler = new ListProductsHandler(_dbContext);

        // Fetch page 1 (limit 4)
        var page1 = await listHandler.HandleAsync(new ListProductsQuery(Limit: 4), CancellationToken.None);
        page1.IsSuccess.ShouldBeTrue();
        page1.Value.Items.Count.ShouldBe(4);
        page1.Value.NextCursor.ShouldNotBeNull();

        // Fetch page 2 (limit 4) using nextCursor
        var page2 = await listHandler.HandleAsync(new ListProductsQuery(Cursor: page1.Value.NextCursor, Limit: 4), CancellationToken.None);
        page2.IsSuccess.ShouldBeTrue();
        page2.Value.Items.Count.ShouldBe(4);
        page2.Value.NextCursor.ShouldNotBeNull();

        // Fetch page 3 (limit 4)
        var page3 = await listHandler.HandleAsync(new ListProductsQuery(Cursor: page2.Value.NextCursor, Limit: 4), CancellationToken.None);
        page3.IsSuccess.ShouldBeTrue();
        page3.Value.Items.Count.ShouldBe(2); // Remaining 2 items
        page3.Value.NextCursor.ShouldBeNull(); // No next cursor!

        // Assert: no duplicates across pages, total 10 unique IDs
        var allIds = page1.Value.Items
            .Concat(page2.Value.Items)
            .Concat(page3.Value.Items)
            .Select(i => i.Id)
            .ToList();

        allIds.Count.ShouldBe(10);
        allIds.Distinct().Count().ShouldBe(10);
    }

    [Fact]
    public async Task ListProducts_TamperedCursor_ReturnsValidation400()
    {
        var listHandler = new ListProductsHandler(_dbContext);
        var result = await listHandler.HandleAsync(
            new ListProductsQuery(Cursor: "invalid-tampered-cursor-content!@#$"),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("catalog.invalid_cursor");
    }

    [Fact]
    public async Task ListProducts_SearchByNamePrefixOrSku_FiltersCorrectly()
    {
        var p1 = Product.Create("SEARCH-APPLE", "Apple iPhone", "Phone", 999m, _timeProvider).Value;
        var p2 = Product.Create("SEARCH-ORANGE", "Orange Phone", "Phone", 499m, _timeProvider).Value;
        _dbContext.Products.AddRange(p1, p2);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listHandler = new ListProductsHandler(_dbContext);

        // Search by name prefix "Apple"
        var searchName = await listHandler.HandleAsync(new ListProductsQuery(Search: "Apple"), CancellationToken.None);
        searchName.Value.Items.Count.ShouldBe(1);
        searchName.Value.Items[0].Sku.ShouldBe("SEARCH-APPLE");

        // Search by exact SKU "SEARCH-ORANGE"
        var searchSku = await listHandler.HandleAsync(new ListProductsQuery(Search: "search-orange"), CancellationToken.None);
        searchSku.Value.Items.Count.ShouldBe(1);
        searchSku.Value.Items[0].Sku.ShouldBe("SEARCH-ORANGE");
    }

    [Fact]
    public async Task PublicApi_GetProductsAsync_ReturnsOnlyRequestedIds_AndFlagsInactiveProducts()
    {
        var p1 = Product.Create("API-01", "Active Product", "Desc", 100m, _timeProvider).Value;
        var p2 = Product.Create("API-02", "Inactive Product", "Desc", 200m, _timeProvider).Value;
        var p3 = Product.Create("API-03", "Other Product", "Desc", 300m, _timeProvider).Value;

        p2.Deactivate();

        _dbContext.Products.AddRange(p1, p2, p3);
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var api = new CatalogApi(_dbContext);
        var snapshots = await api.GetProductsAsync([p1.Id, p2.Id], CancellationToken.None);

        snapshots.Count.ShouldBe(2);

        var snap1 = snapshots.First(s => s.Id == p1.Id);
        snap1.Sku.ShouldBe("API-01");
        snap1.UnitPrice.ShouldBe(100m);
        snap1.IsActive.ShouldBeTrue();

        var snap2 = snapshots.First(s => s.Id == p2.Id);
        snap2.Sku.ShouldBe("API-02");
        snap2.UnitPrice.ShouldBe(200m);
        snap2.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task PublicApi_GetProductsAsync_Exceeding100Ids_ThrowsArgumentException()
    {
        var api = new CatalogApi(_dbContext);
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();

        await Should.ThrowAsync<ArgumentException>(async () =>
            await api.GetProductsAsync(ids, CancellationToken.None));
    }
}
