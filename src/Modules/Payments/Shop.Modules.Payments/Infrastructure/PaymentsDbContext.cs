using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Infrastructure.Data;
using Shop.Modules.Payments.Domain;

namespace Shop.Modules.Payments.Infrastructure;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : ModuleDbContext(options, "payments")
{
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
