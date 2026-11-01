using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Ordering.Domain;

namespace Shop.Modules.Ordering.Infrastructure;

internal sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("order_lines", "ordering");

        builder.HasKey(l => new { l.OrderId, l.ProductId });

        builder.Property(l => l.OrderId)
            .IsRequired();

        builder.Property(l => l.ProductId)
            .IsRequired();

        builder.Property(l => l.ProductNameSnapshot)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(l => l.UnitPriceSnapshot)
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(l => l.Quantity)
            .IsRequired();
    }
}
