using System.Reflection;
using Shop.BuildingBlocks.Abstractions;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.Modules.Catalog;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Inventory;
using Shop.Modules.Inventory.Contracts;
using Shop.Modules.Notifications;
using Shop.Modules.Notifications.Contracts;
using Shop.Modules.Ordering;
using Shop.Modules.Ordering.Contracts;
using Shop.Modules.Payments;
using Shop.Modules.Payments.Contracts;

namespace Shop.ArchitectureTests;

public static class ArchitectureTestContext
{
    public static readonly IReadOnlyList<Assembly> ModuleImplementationAssemblies =
    [
        typeof(CatalogModule).Assembly,
        typeof(InventoryModule).Assembly,
        typeof(OrderingModule).Assembly,
        typeof(PaymentsModule).Assembly,
        typeof(NotificationsModule).Assembly
    ];

    public static readonly IReadOnlyList<Assembly> ModuleContractsAssemblies =
    [
        typeof(Shop.Modules.Catalog.Contracts.Marker).Assembly,
        typeof(Shop.Modules.Inventory.Contracts.Marker).Assembly,
        typeof(Shop.Modules.Ordering.Contracts.Marker).Assembly,
        typeof(Shop.Modules.Payments.Contracts.Marker).Assembly,
        typeof(Shop.Modules.Notifications.Contracts.Marker).Assembly
    ];

    public static readonly IReadOnlyList<Assembly> BuildingBlocksAssemblies =
    [
        typeof(IIntegrationEvent).Assembly,
        typeof(ModuleDbContext).Assembly
    ];

    public static readonly IReadOnlyList<string> ModuleNames =
    [
        "Catalog",
        "Inventory",
        "Ordering",
        "Payments",
        "Notifications"
    ];
}
