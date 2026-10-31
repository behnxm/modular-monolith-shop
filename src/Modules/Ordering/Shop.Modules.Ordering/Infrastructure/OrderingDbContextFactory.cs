using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Modules.Ordering.Infrastructure;

internal sealed class OrderingDbContextFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<OrderingDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shop;Username=shop_migrator;Password=migrator_secure_password_2026",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "ordering"));

        return new OrderingDbContext(optionsBuilder.Options);
    }
}
