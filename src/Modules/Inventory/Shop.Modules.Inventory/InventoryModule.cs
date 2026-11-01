using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Eventing;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Catalog.Contracts.IntegrationEvents.V1;
using Shop.Modules.Inventory.Application.EventHandlers;
using Shop.Modules.Inventory.Application.GetStock;
using Shop.Modules.Inventory.Application.ListStock;
using Shop.Modules.Inventory.Application.SetStock;
using Shop.Modules.Inventory.Endpoints;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public string Name => "inventory";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Inventory.Contracts.IntegrationEvents.V1.StockReservedIntegrationEvent).Assembly;
    public IReadOnlyCollection<string> RequiredModules => ["catalog"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InventoryDbContext>(configuration, Name);
        services.AddOutboxProcessor<InventoryDbContext>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<InventoryDbContext>>($"outbox_{Name}");

        services.AddScoped<SetStockHandler>();
        services.AddScoped<GetStockHandler>();
        services.AddScoped<ListStockHandler>();

        services.AddIntegrationEventHandler<ProductCreatedIntegrationEvent, ProductCreatedIntegrationEventHandler, InventoryDbContext>("inventory.product-created");
        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, OrderPlacedIntegrationEventHandler, InventoryDbContext>("inventory.order-placed");
        services.AddIntegrationEventHandler<OrderConfirmedIntegrationEvent, OrderConfirmedIntegrationEventHandler, InventoryDbContext>("inventory.order-confirmed");
        services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, OrderCancelledIntegrationEventHandler, InventoryDbContext>("inventory.order-cancelled");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        InventoryEndpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
