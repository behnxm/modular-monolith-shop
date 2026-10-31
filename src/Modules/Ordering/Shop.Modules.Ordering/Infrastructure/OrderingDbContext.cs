using Microsoft.EntityFrameworkCore;
using Shop.BuildingBlocks.Infrastructure.Data;

namespace Shop.Modules.Ordering.Infrastructure;

internal sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options)
    : ModuleDbContext(options, "ordering")
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderingDbContext).Assembly);
    }
}
