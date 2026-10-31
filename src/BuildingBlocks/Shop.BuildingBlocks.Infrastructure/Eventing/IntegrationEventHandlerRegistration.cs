namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public sealed record IntegrationEventHandlerRegistration(
    string ContractName,
    Type EventType,
    Type HandlerType,
    Type DbContextType,
    string HandlerName);
