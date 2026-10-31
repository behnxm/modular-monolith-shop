using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Inventory.Endpoints;
using Shop.Modules.Inventory.Infrastructure;

namespace Shop.Modules.Inventory;

public sealed class InventoryModule : IModule
{
    public string Name => "inventory";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Inventory.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => ["catalog"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InventoryDbContext>(configuration, Name);
        services.AddHostedService<OutboxProcessor<InventoryDbContext>>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<InventoryDbContext>>($"outbox_{Name}");
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
