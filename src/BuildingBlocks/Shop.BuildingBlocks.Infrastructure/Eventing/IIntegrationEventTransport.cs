namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public interface IIntegrationEventTransport
{
    Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken);
}
