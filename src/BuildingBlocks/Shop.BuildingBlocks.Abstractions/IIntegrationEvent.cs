namespace Shop.BuildingBlocks.Abstractions;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOn { get; }
}
