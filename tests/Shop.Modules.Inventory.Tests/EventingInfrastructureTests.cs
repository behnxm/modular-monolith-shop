using System.Text.Json;
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
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Domain;
using Shop.Modules.Inventory.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.Modules.Inventory.Tests;

public sealed class EventingInfrastructureTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private InventoryDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    public EventingInfrastructureTests(PostgreSqlFixture fixture)
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

        var appOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("inventory"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .Options;

        _dbContext = new InventoryDbContext(appOptions);
        await _fixture.ResetSchemasAsync("inventory");
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    [Fact]
    public async Task Outbox_Atomicity_CommitsTogetherWithEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = Guid.CreateVersion7();
        var stockItem = StockItem.Create(productId, 50);
        _dbContext.StockItems.Add(stockItem);

        var integrationEvent = new StockReservedIntegrationEvent(Guid.CreateVersion7());
        _dbContext.Enqueue(integrationEvent, _timeProvider);

        await _dbContext.SaveChangesAsync(ct);

        // Verify both stock item and outbox message are in database
        var savedStock = await _dbContext.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId, ct);
        savedStock.ShouldNotBeNull();

        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Id == integrationEvent.Id, ct);
        outboxMessage.ShouldNotBeNull();
        outboxMessage.ContractName.ShouldBe("inventory.stock-reserved.v1");
        outboxMessage.ProcessedOn.ShouldBeNull();
    }

    [Fact]
    public async Task Outbox_Rollback_DiscardsBothEntityAndOutbox()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = Guid.CreateVersion7();
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);

        var stockItem = StockItem.Create(productId, 50);
        _dbContext.StockItems.Add(stockItem);

        var integrationEvent = new StockReservedIntegrationEvent(Guid.CreateVersion7());
        _dbContext.Enqueue(integrationEvent, _timeProvider);

        await _dbContext.SaveChangesAsync(ct);
        await tx.RollbackAsync(ct);

        var savedStock = await _dbContext.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId, ct);
        savedStock.ShouldBeNull();

        var outboxMessage = await _dbContext.OutboxMessages.FirstOrDefaultAsync(m => m.Id == integrationEvent.Id, ct);
        outboxMessage.ShouldBeNull();
    }

    [Fact]
    public async Task OutboxProcessor_ProcessBatchAsync_DispatchesAndMarksProcessed()
    {
        var ct = TestContext.Current.CancellationToken;
        var integrationEvent = new StockReservedIntegrationEvent(Guid.CreateVersion7());
        _dbContext.Enqueue(integrationEvent, _timeProvider);
        await _dbContext.SaveChangesAsync(ct);

        var transport = new InMemoryEventCapture();
        using var sp = CreateServiceProvider(transport);
        var config = new ConfigurationBuilder().Build();
        var processor = new OutboxProcessor<InventoryDbContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            transport,
            config,
            _timeProvider,
            NullLogger<OutboxProcessor<InventoryDbContext>>.Instance);

        var processedCount = await processor.ProcessBatchAsync(ct);
        processedCount.ShouldBe(1);

        // Verify transport received event
        transport.PublishedEnvelopes.Count.ShouldBe(1);
        transport.PublishedEnvelopes[0].ContractName.ShouldBe("inventory.stock-reserved.v1");

        // Verify outbox message updated to processed
        var message = await _dbContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == integrationEvent.Id, ct);
        message.ProcessedOn.ShouldNotBeNull();
        message.FailedOn.ShouldBeNull();
    }

    [Fact]
    public async Task OutboxProcessor_RetriesAndEventuallyParksMessage_WhenFailing()
    {
        var ct = TestContext.Current.CancellationToken;
        var integrationEvent = new StockReservedIntegrationEvent(Guid.CreateVersion7());
        _dbContext.Enqueue(integrationEvent, _timeProvider);
        await _dbContext.SaveChangesAsync(ct);

        var failingTransport = new FailingTransport();
        using var sp = CreateServiceProvider(failingTransport);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Modules:inventory:Outbox:MaxAttempts"] = "2"
            })
            .Build();

        var processor = new OutboxProcessor<InventoryDbContext>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            failingTransport,
            config,
            _timeProvider,
            NullLogger<OutboxProcessor<InventoryDbContext>>.Instance);

        // Attempt 1: should fail and schedule next attempt
        var processed1 = await processor.ProcessBatchAsync(ct);
        processed1.ShouldBe(0);

        var msgAfter1 = await _dbContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == integrationEvent.Id, ct);
        msgAfter1.Attempts.ShouldBe(1);
        msgAfter1.ProcessedOn.ShouldBeNull();
        msgAfter1.FailedOn.ShouldBeNull();

        // Advance time beyond next_attempt_on
        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        // Attempt 2: reaches max attempts (2) -> parked (FailedOn set)
        var processed2 = await processor.ProcessBatchAsync(ct);
        processed2.ShouldBe(0);

        var msgAfter2 = await _dbContext.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == integrationEvent.Id, ct);
        msgAfter2.Attempts.ShouldBe(2);
        msgAfter2.FailedOn.ShouldNotBeNull();
        msgAfter2.ProcessedOn.ShouldBeNull();
    }

    [Fact]
    public async Task InboxDispatcher_Deduplication_SkipsDuplicateMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddLogging();
        services.AddScoped(_ => CreateAppDbContext());

        var executionCount = 0;
        services.AddScoped<CountingEventHandler>(_ => new CountingEventHandler(() => executionCount++));

        using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var dispatcher = new InboxDispatcher(scopeFactory, _timeProvider, NullLogger<InboxDispatcher>.Instance);

        var eventId = Guid.CreateVersion7();
        var envelope = new IntegrationEventEnvelope(
            eventId,
            "test.event.v1",
            JsonSerializer.Serialize(new StockReservedIntegrationEvent(Guid.CreateVersion7(), eventId, DateTimeOffset.UtcNow)),
            DateTimeOffset.UtcNow,
            null,
            null);

        var reg = new IntegrationEventHandlerRegistration(
            "test.event.v1",
            typeof(StockReservedIntegrationEvent),
            typeof(CountingEventHandler),
            typeof(InventoryDbContext),
            "TestCountingHandler");

        // First dispatch: should execute
        await dispatcher.DispatchAsync(envelope, reg, ct);
        executionCount.ShouldBe(1);

        // Second dispatch: should deduplicate and skip
        await dispatcher.DispatchAsync(envelope, reg, ct);
        executionCount.ShouldBe(1);

        // Verify exactly one inbox message exists
        var inboxRows = await _dbContext.InboxMessages
            .Where(m => m.MessageId == eventId && m.HandlerName == "TestCountingHandler")
            .ToListAsync(ct);
        inboxRows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task InboxDispatcher_Rollback_OnHandlerException()
    {
        var ct = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_timeProvider);
        services.AddLogging();
        services.AddScoped(_ => CreateAppDbContext());
        services.AddScoped<FailingEventHandler>();

        using var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var dispatcher = new InboxDispatcher(scopeFactory, _timeProvider, NullLogger<InboxDispatcher>.Instance);

        var eventId = Guid.CreateVersion7();
        var envelope = new IntegrationEventEnvelope(
            eventId,
            "test.failing.v1",
            JsonSerializer.Serialize(new StockReservedIntegrationEvent(Guid.CreateVersion7(), eventId, DateTimeOffset.UtcNow)),
            DateTimeOffset.UtcNow,
            null,
            null);

        var reg = new IntegrationEventHandlerRegistration(
            "test.failing.v1",
            typeof(StockReservedIntegrationEvent),
            typeof(FailingEventHandler),
            typeof(InventoryDbContext),
            "FailingHandler");

        // Handler throws: dispatcher should rollback and rethrow
        await Should.ThrowAsync<InvalidOperationException>(() =>
            dispatcher.DispatchAsync(envelope, reg, ct));

        // Verify inbox message was rolled back
        var inboxRow = await _dbContext.InboxMessages
            .FirstOrDefaultAsync(m => m.MessageId == eventId && m.HandlerName == "FailingHandler", ct);
        inboxRow.ShouldBeNull();
    }

    private InventoryDbContext CreateAppDbContext()
    {
        var appOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("inventory"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"))
            .Options;
        return new InventoryDbContext(appOptions);
    }

    private ServiceProvider CreateServiceProvider(IIntegrationEventTransport transport)
    {
        var services = new ServiceCollection();
        services.AddSingleton(transport);
        services.AddScoped(_ => CreateAppDbContext());
        return services.BuildServiceProvider();
    }

    private sealed class FailingTransport : IIntegrationEventTransport
    {
        public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Transport network failure simulated");
        }
    }

    private sealed class CountingEventHandler(Action onExecute) : IIntegrationEventHandler<StockReservedIntegrationEvent>
    {
        public Task HandleAsync(StockReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            onExecute();
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEventHandler : IIntegrationEventHandler<StockReservedIntegrationEvent>
    {
        public Task HandleAsync(StockReservedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated business handler failure");
        }
    }
}
