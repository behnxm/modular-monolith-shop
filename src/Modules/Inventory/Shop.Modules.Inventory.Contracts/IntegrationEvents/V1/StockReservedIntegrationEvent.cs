using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;

[IntegrationEventName("inventory.stock-reserved.v1")]
public sealed record StockReservedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public StockReservedIntegrationEvent(Guid orderId, Guid id, DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        Id = id;
        OccurredOn = occurredOn;
    }

    public StockReservedIntegrationEvent(Guid orderId)
        : this(orderId, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
