using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.BuildingBlocks.Infrastructure.Modules;

namespace Shop.Host;

public static class CliRunner
{
    public static async Task<int?> HandleCliSwitchesAsync(
        string[] args,
        IHost app,
        IReadOnlyList<IModule> enabledModules,
        ILogger logger)
    {
        if (args.Contains("--healthcheck"))
        {
            return await ExecuteHealthCheckAsync(logger);
        }

        if (args.Contains("--migrate"))
        {
            return await ExecuteMigrationsAsync(app.Services, enabledModules, logger);
        }

        if (args.Contains("--seed-demo"))
        {
            return await ExecuteSeedDemoAsync(app.Services, enabledModules, logger);
        }

        var devKeyIndex = Array.IndexOf(args, "--generate-dev-key");
        if (devKeyIndex >= 0 && devKeyIndex + 1 < args.Length)
        {
            return ExecuteGenerateDevKey(args[devKeyIndex + 1], logger);
        }

        return null;
    }

    public static async Task<int> ExecuteHealthCheckAsync(ILogger? logger = null)
    {
        try
        {
            var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
            var url = $"http://127.0.0.1:{port}/health/live";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                if (logger is not null)
                {
                    logger.LogInformation("Liveness healthcheck passed (HTTP {StatusCode}).", (int)response.StatusCode);
                }
                else
                {
                    Console.WriteLine($"Liveness healthcheck passed (HTTP {(int)response.StatusCode}).");
                }
                return 0;
            }

            if (logger is not null)
            {
                logger.LogError("Liveness healthcheck failed with HTTP {StatusCode}.", (int)response.StatusCode);
            }
            else
            {
                Console.Error.WriteLine($"Liveness healthcheck failed with HTTP {(int)response.StatusCode}.");
            }
            return 1;
        }
        catch (Exception ex)
        {
            if (logger is not null)
            {
                logger.LogError(ex, "Liveness healthcheck probe failed to connect.");
            }
            else
            {
                Console.Error.WriteLine($"Liveness healthcheck probe failed to connect: {ex.Message}");
            }
            return 1;
        }
    }

    public static int ExecuteGenerateDevKey(string targetPath, ILogger? logger = null)
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(targetPath, ecdsa.ExportECPrivateKeyPem());
        if (logger is not null)
        {
            logger.LogInformation("Generated development ECDSA signing key at {Path}", targetPath);
        }
        else
        {
            Console.WriteLine($"Generated development ECDSA signing key at {targetPath}");
        }
        return 0;
    }

    private static async Task<int> ExecuteMigrationsAsync(
        IServiceProvider services,
        IReadOnlyList<IModule> enabledModules,
        ILogger logger)
    {
        try
        {
            logger.LogInformation("Applying migrations for all enabled modules in deterministic order...");
            await MigrationRunner.ApplyMigrationsAsync(services, enabledModules, logger, CancellationToken.None);
            logger.LogInformation("Database migrations completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error executing database migrations.");
            return 1;
        }
    }

    private static async Task<int> ExecuteSeedDemoAsync(
        IServiceProvider services,
        IReadOnlyList<IModule> enabledModules,
        ILogger logger)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        if (env.IsProduction())
        {
            logger.LogError("FATAL: --seed-demo is strictly prohibited in Production environments!");
            return 1;
        }

        try
        {
            logger.LogInformation("Running demo seeders for enabled modules...");
            using var scope = services.CreateScope();
            var seeders = scope.ServiceProvider.GetServices<IModuleSeeder>().ToList();

            foreach (var module in enabledModules)
            {
                var seeder = seeders.FirstOrDefault(s => s.ModuleName.Equals(module.Name, StringComparison.OrdinalIgnoreCase));
                if (seeder is not null)
                {
                    logger.LogInformation("Seeding module: {ModuleName}", module.Name);
                    await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
                }
            }

            logger.LogInformation("Demo seeding completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fatal error running demo seeders.");
            return 1;
        }
    }
}
