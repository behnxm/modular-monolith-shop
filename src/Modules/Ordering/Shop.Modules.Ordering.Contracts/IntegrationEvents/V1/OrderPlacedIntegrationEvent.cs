using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

[IntegrationEventName("ordering.order-placed.v1")]
public sealed record OrderPlacedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public OrderPlacedIntegrationEvent(
        Guid orderId,
        string customerId,
        IReadOnlyList<OrderLineSnapshot> lines,
        decimal totalAmount,
        string currency,
        Guid id,
        DateTimeOffset occurredOn)
    {
        OrderId = orderId;
        CustomerId = customerId;
        Lines = lines;
        TotalAmount = totalAmount;
        Currency = currency;
        Id = id;
        OccurredOn = occurredOn;
    }

    public OrderPlacedIntegrationEvent(
        Guid orderId,
        string customerId,
        IReadOnlyList<OrderLineSnapshot> lines,
        decimal totalAmount,
        string currency)
        : this(orderId, customerId, lines, totalAmount, currency, Guid.CreateVersion7(), DateTimeOffset.UtcNow)
    {
    }

    public Guid OrderId { get; init; }
    public string CustomerId { get; init; }
    public IReadOnlyList<OrderLineSnapshot> Lines { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; }
    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
}
