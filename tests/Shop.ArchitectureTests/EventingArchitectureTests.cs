using System.Reflection;
using System.Text.RegularExpressions;
using Shop.BuildingBlocks.Abstractions;
using Shouldly;
using Xunit;

namespace Shop.ArchitectureTests;

public class EventingArchitectureTests
{
    private static readonly Regex ContractNameRegex = new(@"^[a-z]+\.[a-z-]+\.v[0-9]+$", RegexOptions.Compiled);

    [Fact]
    public void A6_IntegrationEvents_ConformToContractRules()
    {
        var registeredContractNames = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < ArchitectureTestContext.ModuleContractsAssemblies.Count; i++)
        {
            var contractsAssembly = ArchitectureTestContext.ModuleContractsAssemblies[i];
            var moduleName = ArchitectureTestContext.ModuleNames[i].ToLowerInvariant();

            var eventTypes = contractsAssembly.GetTypes()
                .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();

            foreach (var type in eventTypes)
            {
                // Must be sealed
                type.IsSealed.ShouldBeTrue($"Event type '{type.FullName}' must be sealed.");

                // Name must end with IntegrationEvent
                type.Name.ShouldEndWith("IntegrationEvent", Case.Sensitive, $"Event type '{type.Name}' must end with 'IntegrationEvent'.");

                // Namespace must end with .IntegrationEvents.V1
                type.Namespace.ShouldNotBeNull();
                type.Namespace.ShouldEndWith(".IntegrationEvents.V1", Case.Sensitive, $"Event type '{type.FullName}' must be in namespace ending with '.IntegrationEvents.V1'.");

                // Must have [IntegrationEventName]
                var attr = type.GetCustomAttribute<IntegrationEventNameAttribute>();
                attr.ShouldNotBeNull($"Event type '{type.FullName}' must be decorated with [IntegrationEventName].");

                var contractName = attr.Name;
                ContractNameRegex.IsMatch(contractName).ShouldBeTrue(
                    $"Contract name '{contractName}' on '{type.Name}' does not match regex '^[a-z]+\\.[a-z-]+\\.v[0-9]+$'.");

                contractName.StartsWith($"{moduleName}.", StringComparison.Ordinal).ShouldBeTrue(
                    $"Contract name '{contractName}' must start with module prefix '{moduleName}.'.");

                registeredContractNames.Add(contractName).ShouldBeTrue(
                    $"Duplicate contract name '{contractName}' detected on '{type.FullName}'.");
            }
        }
    }

    [Fact]
    public void A7_EventHandlers_AreInternalSealed_AndHandleContractsEventsOnly()
    {
        var contractsEventTypes = ArchitectureTestContext.ModuleContractsAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IIntegrationEvent).IsAssignableFrom(t))
            .ToHashSet();

        foreach (var assembly in ArchitectureTestContext.ModuleImplementationAssemblies)
        {
            var handlerTypes = assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)))
                .ToList();

            foreach (var handler in handlerTypes)
            {
                handler.IsNotPublic.ShouldBeTrue($"Event handler '{handler.FullName}' must be internal.");
                handler.IsSealed.ShouldBeTrue($"Event handler '{handler.FullName}' must be sealed.");
                handler.Name.ShouldEndWith("Handler", Case.Sensitive, $"Event handler '{handler.Name}' must end with 'Handler'.");

                var handledInterfaces = handler.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>));

                foreach (var iface in handledInterfaces)
                {
                    var eventType = iface.GetGenericArguments()[0];
                    contractsEventTypes.Contains(eventType).ShouldBeTrue(
                        $"Handler '{handler.FullName}' handles event '{eventType.FullName}', which does not come from a Contracts assembly.");
                }
            }
        }
    }
}
