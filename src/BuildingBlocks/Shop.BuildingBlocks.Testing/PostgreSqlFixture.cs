using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Shop.BuildingBlocks.Testing;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const string DefaultDatabase = "shop";
    public const string MigratorPassword = "migrator_secure_password_2026";
    public const string AppPassword = "app_secure_password_2026";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17.4-alpine")
        .WithDatabase(DefaultDatabase)
        .WithUsername("postgres")
        .WithPassword("postgres_super_admin_pass")
        .Build();

    public string SuperUserConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await InitializeRolesAndSchemasAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public string GetMigratorConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(SuperUserConnectionString)
        {
            Username = "shop_migrator",
            Password = MigratorPassword
        };
        return builder.ConnectionString;
    }

    public string GetAppConnectionString(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        var normalized = moduleName.ToLowerInvariant();

        var builder = new NpgsqlConnectionStringBuilder(SuperUserConnectionString)
        {
            Username = $"{normalized}_app",
            Password = AppPassword
        };
        return builder.ConnectionString;
    }

    public async Task ResetSchemasAsync(params string[] schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (schemas.Length == 0)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(GetMigratorConnectionString());
        await connection.OpenAsync();

        var respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = schemas,
            TablesToIgnore = ["__EFMigrationsHistory"]
        });

        await respawner.ResetAsync(connection);
    }

    public static async Task MigrateContextAsync<TContext>(TContext context) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(context);
        await context.Database.MigrateAsync();
    }

    private async Task InitializeRolesAndSchemasAsync()
    {
        await using var connection = new NpgsqlConnection(SuperUserConnectionString);
        await connection.OpenAsync();

        var modules = new[] { "catalog", "inventory", "ordering", "payments", "notifications" };

        var sql = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'shop_migrator') THEN
                    CREATE ROLE shop_migrator LOGIN PASSWORD '{MigratorPassword}';
                END IF;
            END
            $$;

            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            """;

        await using (var cmd = new NpgsqlCommand(sql, connection))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var m in modules)
        {
            var moduleSql = $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '{m}_app') THEN
                        CREATE ROLE {m}_app LOGIN PASSWORD '{AppPassword}';
                    END IF;
                END
                $$;

                CREATE SCHEMA IF NOT EXISTS {m} AUTHORIZATION shop_migrator;
                GRANT USAGE ON SCHEMA {m} TO {m}_app;

                ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA {m}
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {m}_app;

                ALTER DEFAULT PRIVILEGES FOR ROLE shop_migrator IN SCHEMA {m}
                    GRANT USAGE, SELECT ON SEQUENCES TO {m}_app;
                """;

            await using var cmd = new NpgsqlCommand(moduleSql, connection);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
