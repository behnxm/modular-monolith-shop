using System.Collections.Concurrent;
using System.Reflection;
using Shop.BuildingBlocks.Abstractions;

namespace Shop.BuildingBlocks.Infrastructure.Eventing;

public sealed class IntegrationEventRegistry
{
    private readonly ConcurrentDictionary<string, Type> _contractNameToEventType = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<IntegrationEventHandlerRegistration>> _handlersByContractName = new(StringComparer.Ordinal);

    public void RegisterModuleContracts(Assembly contractsAssembly)
    {
        ArgumentNullException.ThrowIfNull(contractsAssembly);

        var eventTypes = contractsAssembly.GetTypes()
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var eventType in eventTypes)
        {
            var attr = eventType.GetCustomAttribute<IntegrationEventNameAttribute>();
            if (attr is null)
            {
                continue;
            }

            if (_contractNameToEventType.TryGetValue(attr.Name, out var existingType) && existingType != eventType)
            {
                throw new InvalidOperationException(
                    $"Duplicate integration event contract name '{attr.Name}' detected between '{existingType.FullName}' and '{eventType.FullName}'.");
            }

            _contractNameToEventType[attr.Name] = eventType;
        }
    }

    public void AddHandlerRegistration(IntegrationEventHandlerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        _handlersByContractName.AddOrUpdate(
            registration.ContractName,
            _ => [registration],
            (_, list) =>
            {
                lock (list)
                {
                    if (!list.Any(r => r.HandlerName == registration.HandlerName && r.HandlerType == registration.HandlerType))
                    {
                        list.Add(registration);
                    }
                }
                return list;
            });
    }

    public Type? GetEventType(string contractName)
    {
        _contractNameToEventType.TryGetValue(contractName, out var type);
        return type;
    }

    public IReadOnlyList<IntegrationEventHandlerRegistration> GetHandlersForContract(string contractName)
    {
        if (_handlersByContractName.TryGetValue(contractName, out var list))
        {
            lock (list)
            {
                return list.ToArray();
            }
        }

        return Array.Empty<IntegrationEventHandlerRegistration>();
    }

    public IReadOnlyCollection<string> RegisteredContracts => _contractNameToEventType.Keys.ToArray();
}
