using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;

[IntegrationEventName("inventory.stock-reservation-failed.v1")]
public sealed record StockReservationFailedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public StockReservationFailedIntegrationEvent(
        Guid orderId,
        string reason,
        IReadOnlyList<Guid> unavailableProductIds,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        Reason = reason;
        UnavailableProductIds = unavailableProductIds;
        Id = id;
        OccurredOn = occurredOn;
    }

    public StockReservationFailedIntegrationEvent(
        Guid orderId,
        string reason,
        IReadOnlyList<Guid> unavailableProductIds)
        : this(orderId, reason, unavailableProductIds, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public string Reason { get; init; }
    public IReadOnlyList<Guid> UnavailableProductIds { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
