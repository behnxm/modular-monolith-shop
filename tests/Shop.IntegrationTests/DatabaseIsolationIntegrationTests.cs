using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shop.BuildingBlocks.Testing;
using Shop.Modules.Catalog.Infrastructure;
using Shop.Modules.Inventory.Infrastructure;
using Shop.Modules.Notifications.Infrastructure;
using Shop.Modules.Ordering.Infrastructure;
using Shop.Modules.Payments.Infrastructure;
using Shouldly;
using Xunit;

namespace Shop.IntegrationTests;

public sealed class DatabaseIsolationIntegrationTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DatabaseIsolationIntegrationTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await MigrateAsync<CatalogDbContext>("catalog");
        await MigrateAsync<InventoryDbContext>("inventory");
        await MigrateAsync<OrderingDbContext>("ordering");
        await MigrateAsync<PaymentsDbContext>("payments");
        await MigrateAsync<NotificationsDbContext>("notifications");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task MigrateAsync<TContext>(string schema) where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(_fixture.GetMigratorConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
            .Options;

        await using var context = (TContext)Activator.CreateInstance(typeof(TContext), options)!;
        await PostgreSqlFixture.MigrateContextAsync(context);
    }

    [Fact]
    public async Task CatalogAppRole_CannotQuery_ForeignModuleSchemas()
    {
        var ct = TestContext.Current.CancellationToken;
        var catalogConnStr = _fixture.GetAppConnectionString("catalog");

        await using var conn = new NpgsqlConnection(catalogConnStr);
        await conn.OpenAsync(ct);

        // Own schema query succeeds
        await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM catalog.products;", conn))
        {
            var count = await cmd.ExecuteScalarAsync(ct);
            count.ShouldNotBeNull();
        }

        // Foreign schema queries fail with PostgresException 42501 (insufficient_privilege)
        var foreignTables = new[]
        {
            "inventory.stock_items",
            "ordering.orders",
            "payments.payment_attempts",
            "notifications.notifications"
        };

        foreach (var table in foreignTables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table};", conn);
            var ex = await Should.ThrowAsync<PostgresException>(async () => await cmd.ExecuteScalarAsync(ct));
            ex.SqlState.ShouldBe("42501", $"Accessing '{table}' from catalog_app should fail with 42501 (insufficient_privilege).");
        }
    }

    [Fact]
    public async Task InventoryAppRole_CannotQuery_ForeignModuleSchemas()
    {
        var ct = TestContext.Current.CancellationToken;
        var inventoryConnStr = _fixture.GetAppConnectionString("inventory");

        await using var conn = new NpgsqlConnection(inventoryConnStr);
        await conn.OpenAsync(ct);

        // Own schema query succeeds
        await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM inventory.stock_items;", conn))
        {
            var count = await cmd.ExecuteScalarAsync(ct);
            count.ShouldNotBeNull();
        }

        // Foreign schema queries fail
        var foreignTables = new[]
        {
            "catalog.products",
            "ordering.orders",
            "payments.payment_attempts",
            "notifications.notifications"
        };

        foreach (var table in foreignTables)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {table};", conn);
            var ex = await Should.ThrowAsync<PostgresException>(async () => await cmd.ExecuteScalarAsync(ct));
            ex.SqlState.ShouldBe("42501", $"Accessing '{table}' from inventory_app should fail with 42501 (insufficient_privilege).");
        }
    }

    [Fact]
    public async Task Database_HasZeroCrossSchemaForeignKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var conn = new NpgsqlConnection(_fixture.SuperUserConnectionString);
        await conn.OpenAsync(ct);

        var sql = """
            SELECT
                kcu1.table_schema AS fk_table_schema,
                kcu1.table_name AS fk_table_name,
                kcu1.column_name AS fk_column_name,
                kcu2.table_schema AS uq_table_schema,
                kcu2.table_name AS uq_table_name,
                kcu2.column_name AS uq_column_name
            FROM information_schema.referential_constraints rc
            JOIN information_schema.key_column_usage kcu1
                ON kcu1.constraint_name = rc.constraint_name
                AND kcu1.constraint_schema = rc.constraint_schema
            JOIN information_schema.key_column_usage kcu2
                ON kcu2.constraint_name = rc.unique_constraint_name
                AND kcu2.constraint_schema = rc.unique_constraint_schema
            WHERE kcu1.table_schema != kcu2.table_schema;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var crossSchemaFks = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            var fromSchema = reader.GetString(0);
            var fromTable = reader.GetString(1);
            var fromCol = reader.GetString(2);
            var toSchema = reader.GetString(3);
            var toTable = reader.GetString(4);
            var toCol = reader.GetString(5);
            crossSchemaFks.Add($"{fromSchema}.{fromTable}.{fromCol} -> {toSchema}.{toTable}.{toCol}");
        }

        crossSchemaFks.ShouldBeEmpty("Zero cross-schema foreign keys are permitted across modules.");
    }
}
