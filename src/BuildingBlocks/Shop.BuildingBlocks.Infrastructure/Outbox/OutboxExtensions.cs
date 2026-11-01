using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.BuildingBlocks.Infrastructure.Outbox;

public static class OutboxExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IServiceCollection AddOutboxProcessor<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        services.AddSingleton<OutboxProcessor<TContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxProcessor<TContext>>());
        return services;
    }

    public static OutboxMessage Enqueue(
        this ModuleDbContext db,
        IIntegrationEvent integrationEvent,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var eventType = integrationEvent.GetType();
        var attr = eventType.GetCustomAttribute<IntegrationEventNameAttribute>()
            ?? throw new InvalidOperationException(
                $"Event type '{eventType.FullName}' is missing the required [IntegrationEventName] attribute.");

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var payloadJson = JsonSerializer.Serialize(integrationEvent, eventType, JsonOptions);

        var currentActivity = Activity.Current;
        var traceParent = currentActivity?.Id;
        var traceState = currentActivity?.TraceStateString;

        var message = new OutboxMessage
        {
            Id = integrationEvent.Id == Guid.Empty ? Guid.NewGuid() : integrationEvent.Id,
            ContractName = attr.Name,
            Payload = payloadJson,
            OccurredOn = integrationEvent.OccurredOn == default ? now : integrationEvent.OccurredOn,
            TraceParent = traceParent,
            TraceState = traceState,
            Attempts = 0,
            NextAttemptOn = now,
            ProcessedOn = null,
            FailedOn = null,
            LastError = null
        };

        db.OutboxMessages.Add(message);
        return message;
    }
}
