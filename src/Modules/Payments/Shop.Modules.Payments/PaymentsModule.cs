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
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;
using Shop.Modules.Payments.Application.EventHandlers;
using Shop.Modules.Payments.Application.GetPaymentAttempt;
using Shop.Modules.Payments.Endpoints;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments;

public sealed class PaymentsModule : IModule
{
    public string Name => "payments";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Payments.Contracts.IntegrationEvents.V1.PaymentSucceededIntegrationEvent).Assembly;
    public IReadOnlyCollection<string> RequiredModules => [];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration, Name);
        services.AddOutboxProcessor<PaymentsDbContext>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<PaymentsDbContext>>($"outbox_{Name}");

        services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        services.AddScoped<GetPaymentAttemptHandler>();

        services.AddIntegrationEventHandler<OrderPlacedIntegrationEvent, OrderPlacedIntegrationEventHandler, PaymentsDbContext>("payments.order-placed");
        services.AddIntegrationEventHandler<StockReservedIntegrationEvent, StockReservedIntegrationEventHandler, PaymentsDbContext>("payments.stock-reserved");
        services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, OrderCancelledIntegrationEventHandler, PaymentsDbContext>("payments.order-cancelled");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PaymentsEndpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
