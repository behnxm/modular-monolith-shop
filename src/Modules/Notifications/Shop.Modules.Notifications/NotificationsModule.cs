using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Notifications.Endpoints;
using Shop.Modules.Notifications.Infrastructure;

namespace Shop.Modules.Notifications;

public sealed class NotificationsModule : IModule
{
    public string Name => "notifications";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Notifications.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => Array.Empty<string>();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<NotificationsDbContext>(configuration, Name);
        services.AddOutboxProcessor<NotificationsDbContext>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<NotificationsDbContext>>($"outbox_{Name}");
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
