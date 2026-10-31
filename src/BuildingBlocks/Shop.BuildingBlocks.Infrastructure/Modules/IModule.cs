using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shop.BuildingBlocks.Infrastructure.Modules;

public interface IModule
{
    string Name { get; }
    Assembly ContractsAssembly { get; }
    IReadOnlyCollection<string> RequiredModules { get; }
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);
}
