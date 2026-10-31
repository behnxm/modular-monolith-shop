using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Inbox;

namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public sealed class InProcessEventTransport(
    IntegrationEventRegistry registry,
    IInboxDispatcher inboxDispatcher,
    ILogger<InProcessEventTransport> logger) : IIntegrationEventTransport
{
    public async Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var handlers = registry.GetHandlersForContract(envelope.ContractName);
        if (handlers.Count == 0)
        {
            logger.LogDebug(
                "No registered handlers for event {ContractName} (MessageId: {MessageId}). Marked processed.",
                envelope.ContractName,
                envelope.Id);
            return;
        }

        foreach (var handlerRegistration in handlers)
        {
            await inboxDispatcher.DispatchAsync(envelope, handlerRegistration, cancellationToken);
        }
    }
}
