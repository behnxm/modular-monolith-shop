using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Health;
using Shop.BuildingBlocks.Infrastructure.Modules;
using Shop.BuildingBlocks.Infrastructure.Outbox;
using Shop.Modules.Catalog.Application.CreateProduct;
using Shop.Modules.Catalog.Application.DeactivateProduct;
using Shop.Modules.Catalog.Application.GetProduct;
using Shop.Modules.Catalog.Application.ListProducts;
using Shop.Modules.Catalog.Application.UpdateProduct;
using Shop.Modules.Catalog.Contracts;
using Shop.Modules.Catalog.Endpoints;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Catalog.PublicApi;

namespace Shop.Modules.Catalog;

public sealed class CatalogModule : IModule
{
    public string Name => "catalog";
    public Assembly ContractsAssembly => typeof(Shop.Modules.Catalog.Contracts.Marker).Assembly;
    public IReadOnlyCollection<string> RequiredModules => Array.Empty<string>();

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CatalogDbContext>(configuration, Name);
        services.AddHostedService<OutboxProcessor<CatalogDbContext>>();
        services.AddHealthChecks()
            .AddCheck<OutboxHealthCheck<CatalogDbContext>>($"outbox_{Name}");

        // Public API
        services.AddScoped<ICatalogApi, CatalogApi>();

        // Application Use Cases
        services.AddScoped<CreateProductHandler>();
        services.AddScoped<UpdateProductHandler>();
        services.AddScoped<DeactivateProductHandler>();
        services.AddScoped<GetProductHandler>();
        services.AddScoped<ListProductsHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CatalogEndpoints.MapEndpoints(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }
}
