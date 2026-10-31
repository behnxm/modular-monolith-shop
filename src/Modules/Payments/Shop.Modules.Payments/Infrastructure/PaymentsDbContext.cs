using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.Modules.Payments.Infrastructure;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options)
    : ModuleDbContext(options, "payments")
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
