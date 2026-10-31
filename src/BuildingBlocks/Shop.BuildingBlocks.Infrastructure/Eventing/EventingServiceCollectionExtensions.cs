using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public static class EventingServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationEventHandler<TEvent, THandler, TContext>(
        this IServiceCollection services,
        string handlerName)
        where TEvent : class, IIntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerName);

        var eventType = typeof(TEvent);
        var attr = eventType.GetCustomAttribute<IntegrationEventNameAttribute>()
            ?? throw new InvalidOperationException($"Event {eventType.FullName} missing [IntegrationEventName] attribute.");

        services.TryAddScoped<THandler>();

        var registration = new IntegrationEventHandlerRegistration(
            attr.Name,
            eventType,
            typeof(THandler),
            typeof(TContext),
            handlerName);

        services.AddSingleton(registration);

        return services;
    }
}
