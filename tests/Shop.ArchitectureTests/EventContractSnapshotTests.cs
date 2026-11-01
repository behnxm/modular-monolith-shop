using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Notifications.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;
using Shouldly;
using Xunit;

namespace Shop.ArchitectureTests;

public sealed class EventContractSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid FixedId = Guid.Parse("01940000-0000-7000-8000-000000000001");
    private static readonly DateTimeOffset FixedTime = DateTimeOffset.Parse("2026-11-01T12:00:00+00:00", CultureInfo.InvariantCulture);
    private static readonly Guid FixedProductId = Guid.Parse("01940000-0000-7000-8000-000000000002");
    private static readonly Guid FixedOrderId = Guid.Parse("01940000-0000-7000-8000-000000000010");
    private static readonly Guid FixedPaymentId = Guid.Parse("01940000-0000-7000-8000-000000000020");
    private static readonly Guid FixedNotificationId = Guid.Parse("01940000-0000-7000-8000-000000000030");

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

    private static void AssertContractSnapshot<TEvent>(string contractName, TEvent sampleEvent)
        where TEvent : class, IIntegrationEvent
    {
        var root = GetRepositoryRoot();
        var snapshotPath = Path.Combine(root, "docs", "contracts", "events", $"{contractName}.json");
        File.Exists(snapshotPath).ShouldBeTrue($"Snapshot file '{snapshotPath}' must exist.");

        var expectedJson = File.ReadAllText(snapshotPath).Trim().Replace("\r\n", "\n");
        var actualJson = JsonSerializer.Serialize(sampleEvent, JsonOptions).Trim().Replace("\r\n", "\n");

        actualJson.ShouldBe(expectedJson, $"Serialized contract for '{contractName}' does not match snapshot file.");

        // Verify round-trip deserialization
        var deserialized = JsonSerializer.Deserialize<TEvent>(expectedJson, JsonOptions);
        deserialized.ShouldNotBeNull();
        deserialized.Id.ShouldBe(sampleEvent.Id);
        deserialized.OccurredOn.ShouldBe(sampleEvent.OccurredOn);
    }

    [Fact]
    public void Catalog_ProductCreated_MatchesSnapshot()
    {
        var evt = new ProductCreatedIntegrationEvent(
            FixedId,
            FixedTime,
            FixedProductId,
            "PROD-SAMPLE-01",
            "Sample Product");

        AssertContractSnapshot("catalog.product-created.v1", evt);
    }

    [Fact]
    public void Ordering_OrderPlaced_MatchesSnapshot()
    {
        var lines = new[] { new OrderLineSnapshot(FixedProductId, 2) };
        var evt = new OrderPlacedIntegrationEvent(
            FixedOrderId,
            "cust_sample_123",
            lines,
            99.98m,
            "USD",
            FixedId,
            FixedTime);

        AssertContractSnapshot("ordering.order-placed.v1", evt);
    }

    [Fact]
    public void Inventory_StockReserved_MatchesSnapshot()
    {
        var evt = new StockReservedIntegrationEvent(FixedOrderId, FixedId, FixedTime);
        AssertContractSnapshot("inventory.stock-reserved.v1", evt);
    }

    [Fact]
    public void Inventory_StockReservationFailed_MatchesSnapshot()
    {
        var evt = new StockReservationFailedIntegrationEvent(
            FixedOrderId,
            "Insufficient stock for product",
            [FixedProductId],
            FixedId,
            FixedTime);

        AssertContractSnapshot("inventory.stock-reservation-failed.v1", evt);
    }

    [Fact]
    public void Payments_PaymentSucceeded_MatchesSnapshot()
    {
        var evt = new PaymentSucceededIntegrationEvent(
            FixedOrderId,
            FixedPaymentId,
            99.98m,
            "USD",
            FixedId,
            FixedTime);

        AssertContractSnapshot("payments.payment-succeeded.v1", evt);
    }

    [Fact]
    public void Payments_PaymentFailed_MatchesSnapshot()
    {
        var evt = new PaymentFailedIntegrationEvent(
            FixedOrderId,
            "Card declined by issuing bank",
            FixedId,
            FixedTime);

        AssertContractSnapshot("payments.payment-failed.v1", evt);
    }

    [Fact]
    public void Payments_PaymentRefunded_MatchesSnapshot()
    {
        var evt = new PaymentRefundedIntegrationEvent(
            FixedOrderId,
            FixedPaymentId,
            FixedId,
            FixedTime);

        AssertContractSnapshot("payments.payment-refunded.v1", evt);
    }

    [Fact]
    public void Ordering_OrderConfirmed_MatchesSnapshot()
    {
        var evt = new OrderConfirmedIntegrationEvent(
            FixedOrderId,
            "cust_sample_123",
            99.98m,
            "USD",
            FixedId,
            FixedTime);

        AssertContractSnapshot("ordering.order-confirmed.v1", evt);
    }

    [Fact]
    public void Ordering_OrderCancelled_MatchesSnapshot()
    {
        var evt = new OrderCancelledIntegrationEvent(
            FixedOrderId,
            "cust_sample_123",
            "Customer requested cancellation before shipment",
            FixedId,
            FixedTime);

        AssertContractSnapshot("ordering.order-cancelled.v1", evt);
    }

    [Fact]
    public void Notifications_NotificationRecorded_MatchesSnapshot()
    {
        var evt = new NotificationRecordedIntegrationEvent(
            FixedId,
            FixedTime,
            FixedNotificationId,
            "cust_sample_123",
            "OrderConfirmed");

        AssertContractSnapshot("notifications.notification-recorded.v1", evt);
    }
}
