using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Inbox;

namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public sealed class InProcessEventTransport : IIntegrationEventTransport
{
    private readonly IntegrationEventRegistry _registry;
    private readonly IInboxDispatcher _inboxDispatcher;
    private readonly ILogger<InProcessEventTransport> _logger;

    public InProcessEventTransport(
        IntegrationEventRegistry registry,
        IEnumerable<IntegrationEventHandlerRegistration> registrations,
        IInboxDispatcher inboxDispatcher,
        ILogger<InProcessEventTransport> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _inboxDispatcher = inboxDispatcher ?? throw new ArgumentNullException(nameof(inboxDispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (registrations is not null)
        {
            foreach (var registration in registrations)
            {
                _registry.AddHandlerRegistration(registration);
            }
        }
    }

    public async Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var handlers = _registry.GetHandlersForContract(envelope.ContractName);
        if (handlers.Count == 0)
        {
            _logger.LogDebug(
                "No registered handlers for event {ContractName} (MessageId: {MessageId}). Marked processed.",
                envelope.ContractName,
                envelope.Id);
            return;
        }

        foreach (var handlerRegistration in handlers)
        {
            await _inboxDispatcher.DispatchAsync(envelope, handlerRegistration, cancellationToken);
        }
    }
}
