using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Eventing;
using Shop.BuildingBlocks.Infrastructure.Inbox;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Catalog.Domain;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Catalog.PublicApi;
using Shop.Modules.Inventory;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Notifications;
using Shop.Modules.Notifications.Domain;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering;
using Shop.Modules.Ordering.Application.CancelOrder;
using Shop.Modules.Ordering.Application.GetOrder;
using Shop.Modules.Ordering.Application.PlaceOrder;
using Shop.Modules.Ordering.Domain;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments;
using Shop.Modules.Payments.Domain;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.IntegrationTests;

public sealed class OrderSagaIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero));
    private ServiceProvider _serviceProvider = null!;

    public OrderSagaIntegrationTests(PostgreSqlFixture fixture)
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

        // 2. Setup DI with all modules, handlers, and eventing
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddLogging();
        services.AddScoped<TestCurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<TestCurrentUser>());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:catalog"] = _fixture.GetAppConnectionString("catalog"),
                ["ConnectionStrings:inventory"] = _fixture.GetAppConnectionString("inventory"),
                ["ConnectionStrings:ordering"] = _fixture.GetAppConnectionString("ordering"),
                ["ConnectionStrings:payments"] = _fixture.GetAppConnectionString("payments"),
                ["ConnectionStrings:notifications"] = _fixture.GetAppConnectionString("notifications"),
                ["Modules:payments:ApprovalLimit"] = "10000.00"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // Eventing infrastructure
        var registry = new IntegrationEventRegistry();
        registry.RegisterModuleContracts(typeof(Shop.Modules.Catalog.Contracts.IntegrationEvents.V1.ProductCreatedIntegrationEvent).Assembly);
        registry.RegisterModuleContracts(typeof(Shop.Modules.Inventory.Contracts.IntegrationEvents.V1.StockReservedIntegrationEvent).Assembly);
        registry.RegisterModuleContracts(typeof(Shop.Modules.Ordering.Contracts.IntegrationEvents.V1.OrderPlacedIntegrationEvent).Assembly);
        registry.RegisterModuleContracts(typeof(Shop.Modules.Payments.Contracts.IntegrationEvents.V1.PaymentSucceededIntegrationEvent).Assembly);
        registry.RegisterModuleContracts(typeof(Shop.Modules.Notifications.Contracts.IntegrationEvents.V1.NotificationRecordedIntegrationEvent).Assembly);

        services.AddSingleton(registry);
        services.AddSingleton<IInboxDispatcher, InboxDispatcher>();
        services.AddSingleton<IIntegrationEventTransport, InProcessEventTransport>();

        // Register Modules
        new CatalogModule().ConfigureServices(services, configuration);
        new InventoryModule().ConfigureServices(services, configuration);
        new OrderingModule().ConfigureServices(services, configuration);
        new PaymentsModule().ConfigureServices(services, configuration);
        new NotificationsModule().ConfigureServices(services, configuration);

        _serviceProvider = services.BuildServiceProvider();

        // 3. Reset schemas
        await _fixture.ResetSchemasAsync("catalog", "inventory", "ordering", "payments", "notifications");
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
        await _serviceProvider.DisposeAsync();
    }

    private async Task DrainOutboxesAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<int>>[] outboxProcessors =
        [
            ct => _serviceProvider.GetRequiredService<OutboxProcessor<CatalogDbContext>>().ProcessBatchAsync(ct),
            ct => _serviceProvider.GetRequiredService<OutboxProcessor<InventoryDbContext>>().ProcessBatchAsync(ct),
            ct => _serviceProvider.GetRequiredService<OutboxProcessor<OrderingDbContext>>().ProcessBatchAsync(ct),
            ct => _serviceProvider.GetRequiredService<OutboxProcessor<PaymentsDbContext>>().ProcessBatchAsync(ct),
            ct => _serviceProvider.GetRequiredService<OutboxProcessor<NotificationsDbContext>>().ProcessBatchAsync(ct)
        ];

        var iterations = 0;
        const int maxIterations = 20;

        while (iterations++ < maxIterations)
        {
            var processedAny = false;
            foreach (var processor in outboxProcessors)
            {
                var processed = await processor(cancellationToken);
                if (processed > 0)
                {
                    processedAny = true;
                }
            }

            if (!processedAny)
            {
                break;
            }
        }
    }

    [Fact]
    public async Task Saga_HappyPath_OrderConfirmed_AndStockCommitted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var catalogDb = sp.GetRequiredService<CatalogDbContext>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var orderingDb = sp.GetRequiredService<OrderingDbContext>();
        var paymentsDb = sp.GetRequiredService<PaymentsDbContext>();

        // 1. Seed Product in Catalog
        var product = Product.Create("SKU-HAPPY-01", "Smartphone", "High end smartphone", 50.00m, _timeProvider).Value;
        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync(ct);

        // 2. Seed Stock in Inventory
        var stock = StockItem.Create(product.Id, 20);
        inventoryDb.StockItems.Add(stock);
        await inventoryDb.SaveChangesAsync(ct);

        // 3. Customer places order for 2 smartphones ($100)
        var placeHandler = sp.GetRequiredService<PlaceOrderHandler>();
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 2)]);
        var placeResult = await placeHandler.HandleAsync("cust_01", "idemp-happy-01", request, ct);

        placeResult.IsSuccess.ShouldBeTrue();
        var orderId = placeResult.Value.Order.Id;

        // 4. Run saga choreography until settled
        await DrainOutboxesAsync(ct);

        // 5. Verify final states across modules
        // Ordering: Confirmed
        var finalOrder = await orderingDb.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        finalOrder.Status.ShouldBe(OrderStatus.Confirmed);

        // Inventory: Stock committed (OnHand reduced from 20 to 18, Reserved = 0)
        var finalStock = await inventoryDb.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product.Id, ct);
        finalStock.OnHand.ShouldBe(18);
        finalStock.Reserved.ShouldBe(0);

        // Payments: Succeeded
        var finalPayment = await paymentsDb.PaymentAttempts.AsNoTracking().FirstAsync(p => p.OrderId == orderId, ct);
        finalPayment.Status.ShouldBe(PaymentStatus.Succeeded);
        finalPayment.Amount.ShouldBe(100.00m);

        // Notifications: Order confirmed notification recorded
        var notificationsDb = sp.GetRequiredService<NotificationsDbContext>();
        var finalNotification = await notificationsDb.Notifications.AsNoTracking().FirstAsync(n => n.CustomerId == "cust_01", ct);
        finalNotification.Kind.ShouldBe(NotificationKind.OrderConfirmed);
        finalNotification.Subject.ShouldBe("Order Confirmed");
    }

    [Fact]
    public async Task Saga_InsufficientStock_OrderCancelled_AndNoCharge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var catalogDb = sp.GetRequiredService<CatalogDbContext>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var orderingDb = sp.GetRequiredService<OrderingDbContext>();
        var paymentsDb = sp.GetRequiredService<PaymentsDbContext>();

        // 1. Seed Product
        var product = Product.Create("SKU-LOW-01", "Earbuds", "Wireless earbuds", 30.00m, _timeProvider).Value;
        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync(ct);

        // 2. Seed Stock: Only 2 on hand
        var stock = StockItem.Create(product.Id, 2);
        inventoryDb.StockItems.Add(stock);
        await inventoryDb.SaveChangesAsync(ct);

        // 3. Customer places order for 5 earbuds (requires 5, only 2 available)
        var placeHandler = sp.GetRequiredService<PlaceOrderHandler>();
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 5)]);
        var placeResult = await placeHandler.HandleAsync("cust_01", "idemp-low-01", request, ct);

        placeResult.IsSuccess.ShouldBeTrue();
        var orderId = placeResult.Value.Order.Id;

        // 4. Run saga
        await DrainOutboxesAsync(ct);

        // 5. Verify
        // Ordering: Cancelled with insufficient stock reason
        var finalOrder = await orderingDb.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        finalOrder.Status.ShouldBe(OrderStatus.Cancelled);
        finalOrder.CancellationReason.ShouldNotBeNull();
        finalOrder.CancellationReason.ShouldContain("Insufficient stock");

        // Inventory: Stock untouched (2 on hand, 0 reserved)
        var finalStock = await inventoryDb.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product.Id, ct);
        finalStock.OnHand.ShouldBe(2);
        finalStock.Reserved.ShouldBe(0);

        // Payments: Not charged (payment attempt stays pending or cancelled)
        var payment = await paymentsDb.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is not null)
        {
            payment.Status.ShouldNotBe(PaymentStatus.Succeeded);
        }

        // Notifications: Order cancelled notification recorded
        var notificationsDb = sp.GetRequiredService<NotificationsDbContext>();
        var finalNotification = await notificationsDb.Notifications.AsNoTracking().FirstAsync(n => n.CustomerId == "cust_01", ct);
        finalNotification.Kind.ShouldBe(NotificationKind.OrderCancelled);
        finalNotification.Subject.ShouldBe("Order Cancelled");
    }

    [Fact]
    public async Task Saga_PaymentDeclined_OrderCancelled_AndStockReleased()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var catalogDb = sp.GetRequiredService<CatalogDbContext>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var orderingDb = sp.GetRequiredService<OrderingDbContext>();
        var paymentsDb = sp.GetRequiredService<PaymentsDbContext>();

        // 1. Seed Expensive Product: $6,000 each
        var product = Product.Create("SKU-LUX-01", "Server Node", "Enterprise server node", 6000.00m, _timeProvider).Value;
        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync(ct);

        // 2. Seed Stock: 10 on hand
        var stock = StockItem.Create(product.Id, 10);
        inventoryDb.StockItems.Add(stock);
        await inventoryDb.SaveChangesAsync(ct);

        // 3. Customer places order for 2 nodes ($12,000 > $10,000 approval limit)
        var placeHandler = sp.GetRequiredService<PlaceOrderHandler>();
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 2)]);
        var placeResult = await placeHandler.HandleAsync("cust_01", "idemp-lux-01", request, ct);

        placeResult.IsSuccess.ShouldBeTrue();
        var orderId = placeResult.Value.Order.Id;

        // 4. Run saga: Inventory reserves -> Payments declines -> Ordering cancels -> Inventory releases!
        await DrainOutboxesAsync(ct);

        // 5. Verify compensations
        // Ordering: Cancelled
        var finalOrder = await orderingDb.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        finalOrder.Status.ShouldBe(OrderStatus.Cancelled);
        finalOrder.CancellationReason.ShouldNotBeNull();
        finalOrder.CancellationReason.ShouldContain("exceeds maximum approval limit");

        // Payments: Failed
        var finalPayment = await paymentsDb.PaymentAttempts.AsNoTracking().FirstAsync(p => p.OrderId == orderId, ct);
        finalPayment.Status.ShouldBe(PaymentStatus.Failed);

        // Inventory: Compensated and released! OnHand stays 10, Reserved returned to 0!
        var finalStock = await inventoryDb.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product.Id, ct);
        finalStock.OnHand.ShouldBe(10);
        finalStock.Reserved.ShouldBe(0);
    }

    [Fact]
    public async Task Saga_RacingCancellation_OrderCancelled_ReleasesStockAndRefundsPayment()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<TestCurrentUser>().UserId = "cust_01";

        var catalogDb = sp.GetRequiredService<CatalogDbContext>();
        var inventoryDb = sp.GetRequiredService<InventoryDbContext>();
        var orderingDb = sp.GetRequiredService<OrderingDbContext>();
        var paymentsDb = sp.GetRequiredService<PaymentsDbContext>();

        // 1. Seed Product & Stock
        var product = Product.Create("SKU-RACE-01", "Gaming Monitor", "4K OLED Monitor", 500.00m, _timeProvider).Value;
        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync(ct);

        var stock = StockItem.Create(product.Id, 10);
        inventoryDb.StockItems.Add(stock);
        await inventoryDb.SaveChangesAsync(ct);

        // 2. Customer places order
        var placeHandler = sp.GetRequiredService<PlaceOrderHandler>();
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 1)]);
        var placeResult = await placeHandler.HandleAsync("cust_01", "idemp-race-01", request, ct);
        placeResult.IsSuccess.ShouldBeTrue();
        var orderId = placeResult.Value.Order.Id;

        // 3. Customer immediately cancels pending order
        var cancelHandler = sp.GetRequiredService<CancelOrderHandler>();
        var cancelResult = await cancelHandler.HandleAsync(orderId, ct);
        cancelResult.IsSuccess.ShouldBeTrue();

        // 4. Drain all outboxes to settle all choreographed messages
        await DrainOutboxesAsync(ct);

        // 5. Verify final states:
        // Ordering cancelled
        var finalOrder = await orderingDb.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        finalOrder.Status.ShouldBe(OrderStatus.Cancelled);

        // Inventory stock fully available (not locked/reserved)
        var finalStock = await inventoryDb.StockItems.AsNoTracking().FirstAsync(s => s.ProductId == product.Id, ct);
        finalStock.OnHand.ShouldBe(10);
        finalStock.Reserved.ShouldBe(0);

        // Payments: Succeeded payment refunded or pending payment cancelled
        var finalPayment = await paymentsDb.PaymentAttempts.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (finalPayment is not null)
        {
            finalPayment.Status.ShouldBeOneOf(PaymentStatus.Failed, PaymentStatus.Refunded);
        }
    }

    [Fact]
    public async Task Saga_IdempotentOrderPlacement_ReturnsCachedOrderWithoutDuplicateProcessing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var catalogDb = sp.GetRequiredService<CatalogDbContext>();
        var orderingDb = sp.GetRequiredService<OrderingDbContext>();

        // 1. Seed Product
        var product = Product.Create("SKU-IDEMP-01", "Mechanical Keyboard", "RGB Mechanical Keyboard", 80.00m, _timeProvider).Value;
        catalogDb.Products.Add(product);
        await catalogDb.SaveChangesAsync(ct);

        var placeHandler = sp.GetRequiredService<PlaceOrderHandler>();
        var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 1)]);

        // 2. First call
        var firstResult = await placeHandler.HandleAsync("cust_01", "idemp-unique-key-01", request, ct);
        firstResult.IsSuccess.ShouldBeTrue();

        // 3. Duplicate call with identical key and payload
        var secondResult = await placeHandler.HandleAsync("cust_01", "idemp-unique-key-01", request, ct);
        secondResult.IsSuccess.ShouldBeTrue();

        // Verify identical response
        secondResult.Value.Order.Id.ShouldBe(firstResult.Value.Order.Id);
        secondResult.Value.Order.TotalAmount.ShouldBe(firstResult.Value.Order.TotalAmount);

        // Exactly one order exists in DB
        var totalOrders = await orderingDb.Orders.CountAsync(ct);
        totalOrders.ShouldBe(1);

        // 4. Mismatched payload with same idempotency key returns Conflict
        var differentRequest = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 2)]);
        var conflictResult = await placeHandler.HandleAsync("cust_01", "idemp-unique-key-01", differentRequest, ct);
        conflictResult.IsFailure.ShouldBeTrue();
        conflictResult.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Saga_BolaProtection_CustomerCannotAccessOrCancelOtherCustomerOrder()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. Customer 1 places order
        Guid orderId;
        await using (var scope1 = _serviceProvider.CreateAsyncScope())
        {
            var sp1 = scope1.ServiceProvider;
            var catalogDb = sp1.GetRequiredService<CatalogDbContext>();
            var product = Product.Create("SKU-BOLA-01", "Webcam", "1080p HD Webcam", 45.00m, _timeProvider).Value;
            catalogDb.Products.Add(product);
            await catalogDb.SaveChangesAsync(ct);

            var placeHandler = sp1.GetRequiredService<PlaceOrderHandler>();
            var request = new PlaceOrderRequest([new PlaceOrderItemRequest(product.Id, 1)]);
            var placeResult = await placeHandler.HandleAsync("cust_alice", "idemp-bola-01", request, ct);
            placeResult.IsSuccess.ShouldBeTrue();
            orderId = placeResult.Value.Order.Id;
        }

        // 2. Customer Bob attempts to view and cancel Alice's order
        await using var scope2 = _serviceProvider.CreateAsyncScope();
        var sp2 = scope2.ServiceProvider;
        sp2.GetRequiredService<TestCurrentUser>().UserId = "cust_bob";

        var getHandler = sp2.GetRequiredService<GetOrderHandler>();
        var getResult = await getHandler.HandleAsync(orderId, ct);
        getResult.IsFailure.ShouldBeTrue();
        getResult.Error.Type.ShouldBe(ErrorType.NotFound);

        var cancelHandler = sp2.GetRequiredService<CancelOrderHandler>();
        var cancelResult = await cancelHandler.HandleAsync(orderId, ct);
        cancelResult.IsFailure.ShouldBeTrue();
        cancelResult.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);
        public bool HasScope(string scope) => false;
    }
}
