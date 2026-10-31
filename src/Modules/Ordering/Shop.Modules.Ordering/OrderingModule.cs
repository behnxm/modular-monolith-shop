using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Ordering.Endpoints;
using Shop.Modules.Ordering.Infrastructure;

namespace Shop.Modules.Ordering;

public sealed class OrderingModule : IModule
{
    public string Name => "ordering";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Ordering.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => ["catalog"];

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<OrderingDbContext>(configuration, Name);
        services.AddHostedService<OutboxProcessor<OrderingDbContext>>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<OrderingDbContext>>($"outbox_{Name}");
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
