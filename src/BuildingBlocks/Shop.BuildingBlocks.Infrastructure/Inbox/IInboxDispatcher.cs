namespace Shop.BuildingBlocks.Infrastructure.Inbox;

using Shop.BuildingBlocks.Infrastructure.Eventing;

public interface IInboxDispatcher
{
    Task DispatchAsync(
        IntegrationEventEnvelope envelope,
        IntegrationEventHandlerRegistration registration,
        CancellationToken cancellationToken);
}
