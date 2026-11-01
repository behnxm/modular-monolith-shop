using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Payments.Contracts.IntegrationEvents.V1;

[IntegrationEventName("payments.payment-failed.v1")]
public sealed record PaymentFailedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public PaymentFailedIntegrationEvent(
        Guid orderId,
        string reason,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        Reason = reason;
        Id = id;
        OccurredOn = occurredOn;
    }

    public PaymentFailedIntegrationEvent(
        Guid orderId,
        string reason)
        : this(orderId, reason, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public string Reason { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
