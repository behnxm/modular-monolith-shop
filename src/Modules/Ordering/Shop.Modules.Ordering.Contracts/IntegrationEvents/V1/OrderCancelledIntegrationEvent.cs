using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

[IntegrationEventName("ordering.order-cancelled.v1")]
public sealed record OrderCancelledIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public OrderCancelledIntegrationEvent(
        Guid orderId,
        string customerId,
        string reason,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Reason = reason;
        Id = id;
        OccurredOn = occurredOn;
    }

    public OrderCancelledIntegrationEvent(
        Guid orderId,
        string customerId,
        string reason)
        : this(orderId, customerId, reason, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public string CustomerId { get; init; }
    public string Reason { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
