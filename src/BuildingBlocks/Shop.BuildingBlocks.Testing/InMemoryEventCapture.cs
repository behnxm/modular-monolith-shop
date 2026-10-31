using System.Collections.Concurrent;
using System.Text.Json;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Eventing;

namespace Shop.BuildingBlocks.Testing;

public sealed class InMemoryEventCapture : IIntegrationEventTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentBag<IntegrationEventEnvelope> _envelopes = [];
    private readonly IIntegrationEventTransport? _innerTransport;

    public InMemoryEventCapture(IIntegrationEventTransport? innerTransport = null)
    {
        _innerTransport = innerTransport;
    }

    public IReadOnlyList<IntegrationEventEnvelope> PublishedEnvelopes => _envelopes.ToArray();

    public Task PublishAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        _envelopes.Add(envelope);

        return _innerTransport?.PublishAsync(envelope, cancellationToken) ?? Task.CompletedTask;
    }

    public IReadOnlyList<T> GetPublishedEvents<T>() where T : class, IIntegrationEvent
    {
        var result = new List<T>();
        foreach (var envelope in _envelopes)
        {
            try
            {
                var evt = JsonSerializer.Deserialize<T>(envelope.Payload, JsonOptions);
                if (evt is not null)
                {
                    result.Add(evt);
                }
            }
            catch (JsonException)
            {
                // Not this event type; continue
            }
        }

        return result;
    }

    public void Clear()
    {
        _envelopes.Clear();
    }
}
