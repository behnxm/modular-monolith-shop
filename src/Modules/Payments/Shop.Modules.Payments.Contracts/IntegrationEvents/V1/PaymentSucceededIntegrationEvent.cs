using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Payments.Contracts.IntegrationEvents.V1;

[IntegrationEventName("payments.payment-succeeded.v1")]
public sealed record PaymentSucceededIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public PaymentSucceededIntegrationEvent(
        Guid orderId,
        Guid paymentId,
        decimal amount,
        string currency,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        PaymentId = paymentId;
        Amount = amount;
        Currency = currency;
        Id = id;
        OccurredOn = occurredOn;
    }

    public PaymentSucceededIntegrationEvent(
        Guid orderId,
        Guid paymentId,
        decimal amount,
        string currency)
        : this(orderId, paymentId, amount, currency, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public Guid PaymentId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
