using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Payments.Contracts.IntegrationEvents.V1;

[IntegrationEventName("payments.payment-refunded.v1")]
public sealed record PaymentRefundedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public PaymentRefundedIntegrationEvent(
        Guid orderId,
        Guid paymentId,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        PaymentId = paymentId;
        Id = id;
        OccurredOn = occurredOn;
    }

    public PaymentRefundedIntegrationEvent(
        Guid orderId,
        Guid paymentId)
        : this(orderId, paymentId, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public Guid PaymentId { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
