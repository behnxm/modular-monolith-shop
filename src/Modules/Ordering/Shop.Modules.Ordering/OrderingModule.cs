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
using Shop.Modules.Inventory.Contracts.IntegrationEvents.V1;
using Shop.Modules.Ordering.Application.CancelOrder;
using Shop.Modules.Ordering.Application.EventHandlers;
using Shop.Modules.Ordering.Application.GetOrder;
using Shop.Modules.Ordering.Application.ListAllOrders;
using Shop.Modules.Ordering.Application.ListMyOrders;
using Shop.Modules.Ordering.Application.PlaceOrder;
using Shop.Modules.Ordering.Endpoints;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Ordering;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Ordering.Contracts.IntegrationEvents.V1.OrderPlacedIntegrationEvent).Assembly;
    public IReadOnlyCollection<string> RequiredModules => ["catalog", "inventory", "payments"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrderingDbContext>(configuration, Name);
        services.AddOutboxProcessor<OrderingDbContext>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<OrderingDbContext>>($"outbox_{Name}");

        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<GetOrderHandler>();
        services.AddScoped<ListMyOrdersHandler>();
        services.AddScoped<ListAllOrdersHandler>();
        services.AddScoped<CancelOrderHandler>();

        services.AddIntegrationEventHandler<StockReservedIntegrationEvent, StockReservedIntegrationEventHandler, OrderingDbContext>("ordering.stock-reserved");
        services.AddIntegrationEventHandler<StockReservationFailedIntegrationEvent, StockReservationFailedIntegrationEventHandler, OrderingDbContext>("ordering.stock-reservation-failed");
        services.AddIntegrationEventHandler<PaymentSucceededIntegrationEvent, PaymentSucceededIntegrationEventHandler, OrderingDbContext>("ordering.payment-succeeded");
        services.AddIntegrationEventHandler<PaymentFailedIntegrationEvent, PaymentFailedIntegrationEventHandler, OrderingDbContext>("ordering.payment-failed");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        OrderingEndpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
