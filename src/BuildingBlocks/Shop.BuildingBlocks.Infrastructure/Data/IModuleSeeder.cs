namespace Shop.BuildingBlocks.Infrastructure.Data;

public interface IModuleSeeder
{
    string ModuleName { get; }
    Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
