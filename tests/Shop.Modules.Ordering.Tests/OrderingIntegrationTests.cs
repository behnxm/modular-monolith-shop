using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Application;
using Shop.Modules.Ordering.Application.CancelOrder;
using Shop.Modules.Ordering.Application.EventHandlers;
using Shop.Modules.Ordering.Application.GetOrder;
using Shop.Modules.Ordering.Application.ListMyOrders;
using Shop.Modules.Ordering.Application.PlaceOrder;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;
using Shouldly;
using Xunit;

namespace Shop.Modules.Ordering.Tests;

public sealed class OrderingIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private OrderingDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero));

    public OrderingIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var migratorOptions = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ordering"))
            .Options;

        await using var migratorContext = new OrderingDbContext(migratorOptions);
        await PostgreSqlFixture.MigrateContextAsync(migratorContext);

        _dbContext = CreateAppDbContext();
        await _fixture.ResetSchemasAsync("ordering");
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    private OrderingDbContext CreateAppDbContext()
    {
        var appOptions = new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("ordering"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ordering"))
            .Options;
        return new OrderingDbContext(appOptions);
    }

    [Fact]
    public async Task PlaceOrder_Success_StoresOrder_IdempotencyRecord_AndEnqueuesOutboxEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var pId = Guid.CreateVersion7();
        var catalogApi = Substitute.For<ICatalogApi>();
        catalogApi.GetProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns([new ProductSnapshot(pId, "SKU-TEST-1", "Test Product 1", 25.00m, "USD", true)]);

        var handler = new PlaceOrderHandler(_dbContext, catalogApi, _timeProvider);
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(pId, 2)]);
        var key = "order-idemp-01";

        var result = await handler.HandleAsync("cust_01", key, request, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReplayed.ShouldBeFalse();
        result.Value.Order.CustomerId.ShouldBe("cust_01");
        result.Value.Order.TotalAmount.ShouldBe(50.00m);
        result.Value.Order.Status.ShouldBe("Pending");

        // Verify database persistence
        await using var verifyDb = CreateAppDbContext();
        var savedOrder = await verifyDb.Orders.Include(o => o.Lines).FirstAsync(o => o.Id == result.Value.Order.Id, ct);
        savedOrder.CustomerId.ShouldBe("cust_01");
        savedOrder.TotalAmount.ShouldBe(50.00m);
        savedOrder.Lines.Count.ShouldBe(1);

        var idempRecord = await verifyDb.IdempotencyRecords.FirstAsync(r => r.CustomerId == "cust_01" && r.Key == key, ct);
        idempRecord.OrderId.ShouldBe(savedOrder.Id);

        var outboxMessage = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "ordering.order-placed.v1", ct);
        outboxMessage.Payload.ShouldContain(savedOrder.Id.ToString());
    }

    [Fact]
    public async Task PlaceOrder_IdempotentReplay_ReturnsOriginalOrder_WithIsReplayedTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var pId = Guid.CreateVersion7();
        var catalogApi = Substitute.For<ICatalogApi>();
        catalogApi.GetProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns([new ProductSnapshot(pId, "SKU-TEST-1", "Test Product 1", 25.00m, "USD", true)]);

        var handler = new PlaceOrderHandler(_dbContext, catalogApi, _timeProvider);
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(pId, 2)]);
        var key = "order-idemp-replay";

        // First call
        var firstResult = await handler.HandleAsync("cust_01", key, request, ct);
        firstResult.IsSuccess.ShouldBeTrue();
        firstResult.Value.IsReplayed.ShouldBeFalse();

        // Second call (replay)
        var secondResult = await handler.HandleAsync("cust_01", key, request, ct);
        secondResult.IsSuccess.ShouldBeTrue();
        secondResult.Value.IsReplayed.ShouldBeTrue();
        secondResult.Value.Order.Id.ShouldBe(firstResult.Value.Order.Id);
    }

    [Fact]
    public async Task PlaceOrder_UnknownProduct_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var pId = Guid.CreateVersion7();
        var catalogApi = Substitute.For<ICatalogApi>();
        catalogApi.GetProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns([]); // empty return -> unknown product

        var handler = new PlaceOrderHandler(_dbContext, catalogApi, _timeProvider);
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(pId, 1)]);

        var result = await handler.HandleAsync("cust_01", "idemp-unknown", request, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.UnprocessableEntity);
        result.Error.Code.ShouldBe("ordering.unknown_product");
    }

    [Fact]
    public async Task PlaceOrder_InactiveProduct_Returns422()
    {
        var ct = TestContext.Current.CancellationToken;
        var pId = Guid.CreateVersion7();
        var catalogApi = Substitute.For<ICatalogApi>();
        catalogApi.GetProductsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), ct)
            .Returns([new ProductSnapshot(pId, "SKU-INACTIVE", "Inactive Item", 15.00m, "USD", false)]);

        var handler = new PlaceOrderHandler(_dbContext, catalogApi, _timeProvider);
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(pId, 1)]);

        var result = await handler.HandleAsync("cust_01", "idemp-inactive", request, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.UnprocessableEntity);
        result.Error.Code.ShouldBe("ordering.inactive_product");
    }

    [Fact]
    public async Task GetOrder_BOLA_AnotherCustomerReturns404()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        // Caller is cust_02 (other customer)
        var otherCustomer = Substitute.For<ICurrentUser>();
        otherCustomer.UserId.Returns("cust_02");
        otherCustomer.HasScope("shop.admin").Returns(false);

        var handler = new GetOrderHandler(_dbContext, otherCustomer);
        var result = await handler.HandleAsync(order.Id, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetOrder_AdminCanViewAnyCustomerOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        // Caller is admin
        var admin = Substitute.For<ICurrentUser>();
        admin.UserId.Returns("admin_01");
        admin.HasScope("shop.admin").Returns(true);

        var handler = new GetOrderHandler(_dbContext, admin);
        var result = await handler.HandleAsync(order.Id, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task CancelOrder_OwnerCanCancelPending_AndEnqueuesOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        var owner = Substitute.For<ICurrentUser>();
        owner.UserId.Returns("cust_01");
        owner.HasScope("shop.admin").Returns(false);

        var handler = new CancelOrderHandler(_dbContext, owner, _timeProvider);
        var result = await handler.HandleAsync(order.Id, ct);

        result.IsSuccess.ShouldBeTrue();

        await using var verifyDb = CreateAppDbContext();
        var updated = await verifyDb.Orders.FirstAsync(o => o.Id == order.Id, ct);
        updated.Status.ShouldBe(OrderStatus.Cancelled);

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "ordering.order-cancelled.v1", ct);
        outboxMsg.Payload.ShouldContain(order.Id.ToString());
    }

    [Fact]
    public async Task CancelOrder_ConfirmedOrder_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        order.Confirm();
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        var owner = Substitute.For<ICurrentUser>();
        owner.UserId.Returns("cust_01");

        var handler = new CancelOrderHandler(_dbContext, owner, _timeProvider);
        var result = await handler.HandleAsync(order.Id, ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Consumers_PaymentSucceeded_ConfirmsOrder_AndEnqueuesOrderConfirmed()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        var handler = new PaymentSucceededIntegrationEventHandler(_dbContext, _timeProvider, NullLogger<PaymentSucceededIntegrationEventHandler>.Instance);
        var evt = new PaymentSucceededIntegrationEvent(order.Id, Guid.CreateVersion7(), 10m, "USD");

        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb = CreateAppDbContext();
        var updated = await verifyDb.Orders.FirstAsync(o => o.Id == order.Id, ct);
        updated.Status.ShouldBe(OrderStatus.Confirmed);

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "ordering.order-confirmed.v1", ct);
        outboxMsg.Payload.ShouldContain(order.Id.ToString());
    }

    [Fact]
    public async Task Consumers_StockReservationFailed_CancelsOrder_AndEnqueuesOrderCancelled()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Create("cust_01", [(Guid.CreateVersion7(), "P1", 10m, 1)]).Value;
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        var handler = new StockReservationFailedIntegrationEventHandler(_dbContext, _timeProvider, NullLogger<StockReservationFailedIntegrationEventHandler>.Instance);
        var evt = new StockReservationFailedIntegrationEvent(order.Id, "Out of stock", []);

        await handler.HandleAsync(evt, ct);
        await _dbContext.SaveChangesAsync(ct);

        await using var verifyDb = CreateAppDbContext();
        var updated = await verifyDb.Orders.FirstAsync(o => o.Id == order.Id, ct);
        updated.Status.ShouldBe(OrderStatus.Cancelled);

        var outboxMsg = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "ordering.order-cancelled.v1", ct);
        outboxMsg.Payload.ShouldContain(order.Id.ToString());
    }
}
