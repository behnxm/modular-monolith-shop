using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Payments.Endpoints;
using Shop.Modules.Payments.Infrastructure;

namespace Shop.Modules.Payments;

public sealed class PaymentsModule : IModule
{
    public string Name => "payments";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Payments.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => Array.Empty<string>();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PaymentsDbContext>(configuration, Name);
        services.AddHostedService<OutboxProcessor<PaymentsDbContext>>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<PaymentsDbContext>>($"outbox_{Name}");
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
