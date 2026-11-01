using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Notifications.Application.EventHandlers;
using Shop.Modules.Notifications.Application.ListMyNotifications;
using Shop.Modules.Notifications.Domain;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shouldly;
using Xunit;

namespace Shop.Modules.Notifications.Tests;

public sealed class NotificationsIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;
    private NotificationsDbContext _dbContext = null!;
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    public NotificationsIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        var migratorOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
            .Options;

        await using var migratorContext = new NotificationsDbContext(migratorOptions);
        await PostgreSqlFixture.MigrateContextAsync(migratorContext);

        _dbContext = CreateAppDbContext();
        await _fixture.ResetSchemasAsync("notifications");
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    private NotificationsDbContext CreateAppDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(_fixture.GetAppConnectionString("notifications"), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
            .Options;

        return new NotificationsDbContext(options);
    }

    [Fact]
    public async Task Consumers_OrderConfirmed_CreatesNotification_AndEnqueuesNotificationRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new OrderConfirmedIntegrationEventHandler(
            _dbContext,
            _timeProvider,
            NullLogger<OrderConfirmedIntegrationEventHandler>.Instance);

        var orderId = Guid.CreateVersion7();
        var evt = new OrderConfirmedIntegrationEvent(orderId, "cust_notif_01", 150.00m, "USD");

        await handler.HandleAsync(evt, ct);

        await using var verifyDb = CreateAppDbContext();
        var notification = await verifyDb.Notifications.FirstAsync(n => n.CustomerId == "cust_notif_01", ct);
        notification.Kind.ShouldBe(NotificationKind.OrderConfirmed);
        notification.Subject.ShouldBe("Order Confirmed");
        notification.Body.ShouldContain(orderId.ToString());
        notification.Body.ShouldContain("150.00 USD");

        var outbox = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "notifications.notification-recorded.v1", ct);
        outbox.Payload.ShouldContain(notification.Id.ToString());
        outbox.Payload.ShouldContain("cust_notif_01");
    }

    [Fact]
    public async Task Consumers_OrderCancelled_CreatesNotification_AndEnqueuesNotificationRecorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new OrderCancelledIntegrationEventHandler(
            _dbContext,
            _timeProvider,
            NullLogger<OrderCancelledIntegrationEventHandler>.Instance);

        var orderId = Guid.CreateVersion7();
        var evt = new OrderCancelledIntegrationEvent(orderId, "cust_notif_02", "Insufficient stock on hand");

        await handler.HandleAsync(evt, ct);

        await using var verifyDb = CreateAppDbContext();
        var notification = await verifyDb.Notifications.FirstAsync(n => n.CustomerId == "cust_notif_02", ct);
        notification.Kind.ShouldBe(NotificationKind.OrderCancelled);
        notification.Subject.ShouldBe("Order Cancelled");
        notification.Body.ShouldContain(orderId.ToString());
        notification.Body.ShouldContain("Insufficient stock on hand");

        var outbox = await verifyDb.OutboxMessages.FirstAsync(m => m.ContractName == "notifications.notification-recorded.v1", ct);
        outbox.Payload.ShouldContain(notification.Id.ToString());
    }

    [Fact]
    public async Task ListMyNotifications_RetrievesOnlyCurrentCustomerNotificationsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;

        // Seed notifications for cust_alice and cust_bob
        var notifAlice1 = Notification.Create("cust_alice", NotificationKind.OrderConfirmed, "First", "First body", _timeProvider).Value;
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        var notifAlice2 = Notification.Create("cust_alice", NotificationKind.OrderCancelled, "Second", "Second body", _timeProvider).Value;
        var notifBob = Notification.Create("cust_bob", NotificationKind.OrderConfirmed, "Bob's notif", "Bob body", _timeProvider).Value;

        _dbContext.Notifications.AddRange(notifAlice1, notifAlice2, notifBob);
        await _dbContext.SaveChangesAsync(ct);

        var currentUser = new StubCurrentUser { UserId = "cust_alice" };
        var handler = new ListMyNotificationsHandler(_dbContext, currentUser);

        var result = await handler.HandleAsync(ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        // Reverse chronological order (Second then First)
        result.Value[0].Subject.ShouldBe("Second");
        result.Value[1].Subject.ShouldBe("First");
    }

    [Fact]
    public async Task ListMyNotifications_Unauthenticated_ReturnsForbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var currentUser = new StubCurrentUser { UserId = string.Empty };
        var handler = new ListMyNotificationsHandler(_dbContext, currentUser);

        var result = await handler.HandleAsync(ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Forbidden);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(UserId);
        public bool HasScope(string scope) => false;
    }
}
