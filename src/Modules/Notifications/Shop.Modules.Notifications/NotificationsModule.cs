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
using Shop.Modules.Notifications.Application.EventHandlers;
using Shop.Modules.Notifications.Application.ListMyNotifications;
using Shop.Modules.Notifications.Contracts.IntegrationEvents.V1;
using Shop.Modules.Notifications.Endpoints;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Contracts.IntegrationEvents.V1;

namespace Shop.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => "notifications";
    public Assembly ContractsAssembly => typeof(NotificationRecordedIntegrationEvent).Assembly;
    public IReadOnlyCollection<string> RequiredModules => ["ordering"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, Name);
        services.AddOutboxProcessor<NotificationsDbContext>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<NotificationsDbContext>>($"outbox_{Name}");

        services.AddScoped<ListMyNotificationsHandler>();

        services.AddIntegrationEventHandler<OrderConfirmedIntegrationEvent, OrderConfirmedIntegrationEventHandler, NotificationsDbContext>("notifications.order-confirmed");
        services.AddIntegrationEventHandler<OrderCancelledIntegrationEvent, OrderCancelledIntegrationEventHandler, NotificationsDbContext>("notifications.order-cancelled");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        NotificationsEndpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
