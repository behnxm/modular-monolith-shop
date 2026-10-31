using Shop.BuildingBlocks.Abstractions;

namespace Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;

[IntegrationEventName("catalog.product-created.v1")]
public sealed record ProductCreatedIntegrationEvent(
    Guid Id,
    DateTimeOffset OccurredOn,
    Guid ProductId,
    string Sku,
    string Name) : IIntegrationEvent;
