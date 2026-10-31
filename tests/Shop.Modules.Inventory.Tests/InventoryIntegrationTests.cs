using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Application.EventHandlers;
using Shop.Modules.Inventory.Application.GetStock;
using Shop.Modules.Inventory.Application.ListStock;
using Shop.Modules.Inventory.Application.SetStock;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shouldly;
using Xunit;

namespace Shop.Modules.Inventory.Tests;

public sealed class InventoryIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private InventoryDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero));

    public InventoryIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var migratorOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .Options;

        await using var migratorContext = new InventoryDbContext(migratorOptions);
        await PostgreSqlFixture.MigrateContextAsync(migratorContext);

        _dbContext = CreateAppDbContext();
        await _fixture.ResetSchemasAsync("inventory");
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    private InventoryDbContext CreateAppDbContext()
    {
        var appOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("inventory"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .Options;
        return new InventoryDbContext(appOptions);
    }

    [Fact]
    public async Task ProductCreated_Handler_CreatesStockItemWithZeroOnHand_Idempotently()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = Guid.CreateVersion7();
        var handler = new ProductCreatedIntegrationEventHandler(_dbContext);
        var evt = new ProductCreatedIntegrationEvent(productId, "SKU-ITEM-01", "Item 01");

        // First run
        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        var item = await _dbContext.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId, ct);
        item.ShouldNotBeNull();
        item.OnHand.ShouldBe(0);
        item.Reserved.ShouldBe(0);

        // Second run (idempotent replay)
        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        var count = await _dbContext.StockItems.CountAsync(s => s.ProductId == productId, ct);
        count.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentReservations_50Parallel_NeverOversells()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = Guid.CreateVersion7();
        var initialStock = StockItem.Create(productId, 10);
        _dbContext.StockItems.Add(initialStock);
        await _dbContext.SaveChangesAsync(ct);

        var parallelOrders = 50;
        var tasks = new List<Task>();

        for (var i = 0; i < parallelOrders; i++)
        {
            var orderId = Guid.CreateVersion7();
            tasks.Add(Task.Run(async () =>
            {
                await using var localDb = CreateAppDbContext();
                await using var tx = await localDb.Database.BeginTransactionAsync(ct);

                var handler = new OrderPlacedIntegrationEventHandler(localDb, _timeProvider);
                var orderPlacedEvent = new OrderPlacedIntegrationEvent(
                    orderId,
                    "cust_01",
                    [new OrderLineSnapshot(productId, 1)],
                    10.00m,
                    "USD");

                await handler.HandleAsync(orderPlacedEvent, ct);
                await localDb.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }, ct));
        }

        await Task.WhenAll(tasks);

        // Verify stock totals in database
        await using var verifyDb = CreateAppDbContext();
        var finalItem = await verifyDb.StockItems.FirstAsync(s => s.ProductId == productId, ct);
        finalItem.OnHand.ShouldBe(10);
        finalItem.Reserved.ShouldBe(10);
        finalItem.Available.ShouldBe(0);

        // Verify reservations: exactly 10 reserved
        var reservedCount = await verifyDb.Reservations.CountAsync(r => r.Status == ReservationStatus.Reserved, ct);
        reservedCount.ShouldBe(10);

        // Verify outbox messages
        var successEventsCount = await verifyDb.OutboxMessages
            .CountAsync(m => m.ContractName == "inventory.stock-reserved.v1", ct);
        var failedEventsCount = await verifyDb.OutboxMessages
            .CountAsync(m => m.ContractName == "inventory.stock-reservation-failed.v1", ct);

        successEventsCount.ShouldBe(10);
        failedEventsCount.ShouldBe(40);
    }

    [Fact]
    public async Task OrderPlaced_AllOrNothing_RollsBack_WhenAnyLineFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var productA = Guid.CreateVersion7();
        var productB = Guid.CreateVersion7();

        _dbContext.StockItems.Add(StockItem.Create(productA, 10));
        _dbContext.StockItems.Add(StockItem.Create(productB, 0)); // Out of stock
        await _dbContext.SaveChangesAsync(ct);

        var orderId = Guid.CreateVersion7();
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var handler = new OrderPlacedIntegrationEventHandler(_dbContext, _timeProvider);
        var orderPlacedEvent = new OrderPlacedIntegrationEvent(
            orderId,
            "cust_01",
            [
                new OrderLineSnapshot(productA, 2),
                new OrderLineSnapshot(productB, 1)
            ],
            50.00m,
            "USD");

        await handler.HandleAsync(orderPlacedEvent, ct);
        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Verify product A was not reserved (all-or-nothing rollback)
        var itemA = await _dbContext.StockItems.FirstAsync(s => s.ProductId == productA, ct);
        itemA.Reserved.ShouldBe(0);

        // Verify reservation was not created
        var reservation = await _dbContext.Reservations.FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
        reservation.ShouldBeNull();

        // Verify failed event in outbox
        var failedEvent = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(m => m.ContractName == "inventory.stock-reservation-failed.v1", ct);
        failedEvent.ShouldNotBeNull();
        failedEvent.Payload.ShouldContain(productB.ToString());
    }

    [Fact]
    public async Task ReleaseAndCommit_Semantics_FunctionCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = Guid.CreateVersion7();
        _dbContext.StockItems.Add(StockItem.Create(product, 50));
        await _dbContext.SaveChangesAsync(ct);

        // 1. Place order 1 -> Reserved
        var order1Id = Guid.CreateVersion7();
        await using (var tx1 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var placeHandler = new OrderPlacedIntegrationEventHandler(_dbContext, _timeProvider);
            await placeHandler.HandleAsync(
                new OrderPlacedIntegrationEvent(order1Id, "cust_01", [new OrderLineSnapshot(product, 20)], 100m, "USD"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx1.CommitAsync(ct);
        }

        var itemAfterOrder1 = await _dbContext.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product, ct);
        itemAfterOrder1.OnHand.ShouldBe(50);
        itemAfterOrder1.Reserved.ShouldBe(20);

        // 2. Confirm order 1 -> OnHand decreases to 30, Reserved decreases to 0
        await using (var tx2 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var confirmHandler = new OrderConfirmedIntegrationEventHandler(_dbContext);
            await confirmHandler.HandleAsync(
                new OrderConfirmedIntegrationEvent(order1Id, "cust_01", 100m, "USD"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx2.CommitAsync(ct);
        }

        var itemAfterConfirm = await _dbContext.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product, ct);
        itemAfterConfirm.OnHand.ShouldBe(30);
        itemAfterConfirm.Reserved.ShouldBe(0);

        var reservation1 = await _dbContext.Reservations.AsNoTracking().FirstAsync(r => r.OrderId == order1Id, ct);
        reservation1.Status.ShouldBe(ReservationStatus.Committed);

        // 3. Place order 2 -> Reserved 15
        var order2Id = Guid.CreateVersion7();
        await using (var tx3 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var placeHandler = new OrderPlacedIntegrationEventHandler(_dbContext, _timeProvider);
            await placeHandler.HandleAsync(
                new OrderPlacedIntegrationEvent(order2Id, "cust_01", [new OrderLineSnapshot(product, 15)], 75m, "USD"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx3.CommitAsync(ct);
        }

        var itemAfterOrder2 = await _dbContext.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product, ct);
        itemAfterOrder2.OnHand.ShouldBe(30);
        itemAfterOrder2.Reserved.ShouldBe(15);

        // 4. Cancel order 2 -> Reserved decreases to 0, OnHand stays 30
        await using (var tx4 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var cancelHandler = new OrderCancelledIntegrationEventHandler(_dbContext);
            await cancelHandler.HandleAsync(
                new OrderCancelledIntegrationEvent(order2Id, "cust_01", "Changed mind"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx4.CommitAsync(ct);
        }

        var itemAfterCancel = await _dbContext.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product, ct);
        itemAfterCancel.OnHand.ShouldBe(30);
        itemAfterCancel.Reserved.ShouldBe(0);

        var reservation2 = await _dbContext.Reservations.AsNoTracking().FirstAsync(r => r.OrderId == order2Id, ct);
        reservation2.Status.ShouldBe(ReservationStatus.Released);
    }

    [Fact]
    public async Task OrderCancelled_Before_OrderPlaced_Tolerated()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = Guid.CreateVersion7();
        _dbContext.StockItems.Add(StockItem.Create(product, 20));
        await _dbContext.SaveChangesAsync(ct);

        var orderId = Guid.CreateVersion7();

        // 1. OrderCancelled arrives FIRST
        await using (var tx1 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var cancelHandler = new OrderCancelledIntegrationEventHandler(_dbContext);
            await cancelHandler.HandleAsync(
                new OrderCancelledIntegrationEvent(orderId, "cust_01", "User cancelled fast"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx1.CommitAsync(ct);
        }

        // Placeholder reservation created in status Released
        var placeholder = await _dbContext.Reservations.FirstOrDefaultAsync(r => r.OrderId == orderId, ct);
        placeholder.ShouldNotBeNull();
        placeholder.Status.ShouldBe(ReservationStatus.Released);

        // 2. OrderPlaced arrives LATER
        await using (var tx2 = await _dbContext.Database.BeginTransactionAsync(ct))
        {
            var placeHandler = new OrderPlacedIntegrationEventHandler(_dbContext, _timeProvider);
            await placeHandler.HandleAsync(
                new OrderPlacedIntegrationEvent(orderId, "cust_01", [new OrderLineSnapshot(product, 5)], 25m, "USD"),
                ct);
            await _dbContext.SaveChangesAsync(ct);
            await tx2.CommitAsync(ct);
        }

        // Stock should NOT have been reserved
        var item = await _dbContext.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product, ct);
        item.Reserved.ShouldBe(0);
        item.OnHand.ShouldBe(20);
    }

    [Fact]
    public async Task UseCases_SetStock_GetStock_ListStock_WorkAsExpected()
    {
        var ct = TestContext.Current.CancellationToken;
        var setHandler = new SetStockHandler(_dbContext);
        var getHandler = new GetStockHandler(_dbContext);
        var listHandler = new ListStockHandler(_dbContext);

        var pid = Guid.CreateVersion7();

        // 1. SetStock
        var setResult = await setHandler.HandleAsync(pid, new SetStockRequest(100), ct);
        setResult.IsSuccess.ShouldBeTrue();
        setResult.Value.OnHand.ShouldBe(100);
        setResult.Value.Reserved.ShouldBe(0);
        setResult.Value.Available.ShouldBe(100);

        // 2. GetStock
        var getResult = await getHandler.HandleAsync(pid, ct);
        getResult.IsSuccess.ShouldBeTrue();
        getResult.Value.ProductId.ShouldBe(pid);
        getResult.Value.OnHand.ShouldBe(100);

        // 3. ListStock
        var listResult = await listHandler.HandleAsync(new ListStockQuery(10, null), ct);
        listResult.IsSuccess.ShouldBeTrue();
        listResult.Value.Items.Count.ShouldBe(1);
        listResult.Value.Items[0].ProductId.ShouldBe(pid);

        // 4. SetStock negative -> fails
        var negativeResult = await setHandler.HandleAsync(pid, new SetStockRequest(-10), ct);
        negativeResult.IsFailure.ShouldBeTrue();
    }
}
