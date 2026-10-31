using System.Text.Json.Serialization;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;

[IntegrationEventName("catalog.product-created.v1")]
public sealed record ProductCreatedIntegrationEvent : IIntegrationEvent
{
    [JsonConstructor]
    public ProductCreatedIntegrationEvent(
        Guid id,
        DateTimeOffset occurredOn,
        Guid productId,
        string sku,
        string name)
    {
        Id = id;
        OccurredOn = occurredOn;
        ProductId = productId;
        Sku = sku;
        Name = name;
    }

    public ProductCreatedIntegrationEvent(Guid productId, string sku, string name)
        : this(Guid.CreateVersion7(), DateTimeOffset.UtcNow, productId, sku, name)
    {
    }

    public Guid Id { get; init; }
    public DateTimeOffset OccurredOn { get; init; }
    public Guid ProductId { get; init; }
    public string Sku { get; init; }
    public string Name { get; init; }
}
