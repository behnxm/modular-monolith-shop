namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public sealed record IntegrationEventEnvelope(
    Guid Id,
    string ContractName,
    string Payload,
    DateTimeOffset OccurredOn,
    string? TraceParent,
    string? TraceState);
