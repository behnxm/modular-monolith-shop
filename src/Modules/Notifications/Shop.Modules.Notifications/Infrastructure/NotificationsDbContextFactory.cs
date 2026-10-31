using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shop.Modules.Notifications.Infrastructure;

internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<NotificationsDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=shop;Username=shop_migrator;Password=migrator_secure_password_2026",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"));

        return new NotificationsDbContext(optionsBuilder.Options);
    }
}
