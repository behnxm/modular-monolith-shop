using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Inventory.Domain;

namespace Shop.Modules.Inventory.Infrastructure;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stock_items", "inventory");

        builder.HasKey(s => s.ProductId);

        builder.Property(s => s.OnHand)
            .IsRequired();

        builder.Property(s => s.Reserved)
            .IsRequired();

        builder.Property(s => s.Version)
            .IsConcurrencyToken()
            .IsRequired();
    }
}
