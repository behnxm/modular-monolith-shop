using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Modules.Payments.Infrastructure;

internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shop;Username=shop_migrator;Password=migrator_secure_password_2026",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "payments"));

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
