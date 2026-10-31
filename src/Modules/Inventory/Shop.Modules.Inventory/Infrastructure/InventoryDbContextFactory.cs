using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Modules.Inventory.Infrastructure;

internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shop;Username=shop_migrator;Password=migrator_secure_password_2026",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "inventory"));

        return new InventoryDbContext(optionsBuilder.Options);
    }
}
