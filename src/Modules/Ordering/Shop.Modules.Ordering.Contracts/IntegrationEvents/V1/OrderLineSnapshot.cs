namespace Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

public sealed record OrderLineSnapshot(Guid ProductId, int Quantity);
