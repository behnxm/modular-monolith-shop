using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

[IntegrationEventName("ordering.order-confirmed.v1")]
public sealed record OrderConfirmedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public OrderConfirmedIntegrationEvent(
        Guid orderId,
        string customerId,
        decimal totalAmount,
        string currency,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TotalAmount = totalAmount;
        Currency = currency;
        Id = id;
        OccurredOn = occurredOn;
    }

    public OrderConfirmedIntegrationEvent(
        Guid orderId,
        string customerId,
        decimal totalAmount,
        string currency)
        : this(orderId, customerId, totalAmount, currency, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public string CustomerId { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
