using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Modules;

namespace Shop.BuildingBlocks.Infrastructure.Data;

public static class MigrationRunner
{
    private const string AdvisoryLockKey = "shop_modular_monolith_migrations";

    public static async Task ApplyMigrationsAsync(
        IServiceProvider serviceProvider,
        IEnumerable<IModule> modules,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        var sortedModules = modules.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        if (sortedModules.Count == 0)
        {
            logger.LogInformation("No modules enabled for migration.");
            return;
        }

        // Obtain advisory lock using the first module's context or a direct connection
        await using var scope = serviceProvider.CreateAsyncScope();

        // Find any ModuleDbContext to acquire the PostgreSQL advisory lock
        var dbContext = scope.ServiceProvider.GetServices<ModuleDbContext>().FirstOrDefault();
        if (dbContext is null)
        {
            // If contexts are registered as their concrete types, find the first registered concrete context
            foreach (var module in sortedModules)
            {
                var context = scope.ServiceProvider.GetServices<DbContext>()
                    .OfType<ModuleDbContext>()
                    .FirstOrDefault();

                if (context is not null)
                {
                    dbContext = context;
                    break;
                }
            }
        }

        var lockAcquired = false;
        try
        {
            if (dbContext is not null)
            {
                logger.LogInformation("Acquiring PostgreSQL advisory lock for migrations...");
                await dbContext.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_lock(hashtext({0}));",
                    [AdvisoryLockKey],
                    cancellationToken);
                lockAcquired = true;
                logger.LogInformation("PostgreSQL advisory lock acquired.");
            }

            foreach (var module in sortedModules)
            {
                logger.LogInformation("Migrating module: {ModuleName}", module.Name);
                await using var moduleScope = serviceProvider.CreateAsyncScope();
                await module.MigrateAsync(moduleScope.ServiceProvider, cancellationToken);
                logger.LogInformation("Module {ModuleName} migrations applied successfully.", module.Name);
            }
        }
        finally
        {
            if (lockAcquired && dbContext is not null)
            {
                try
                {
                    await dbContext.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_unlock(hashtext({0}));",
                        [AdvisoryLockKey],
                        CancellationToken.None);
                    logger.LogInformation("PostgreSQL advisory lock released.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to release PostgreSQL advisory lock.");
                }
            }
        }
    }
}
