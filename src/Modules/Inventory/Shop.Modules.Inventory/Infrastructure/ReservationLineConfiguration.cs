using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Inventory.Domain;

namespace Shop.Modules.Inventory.Infrastructure;

internal sealed class ReservationLineConfiguration : IEntityTypeConfiguration<ReservationLine>
{
    public void Configure(EntityTypeBuilder<ReservationLine> builder)
    {
        builder.ToTable("reservation_lines", "inventory");

        builder.HasKey(l => new { l.OrderId, l.ProductId });

        builder.Property(l => l.OrderId)
            .IsRequired();

        builder.Property(l => l.ProductId)
            .IsRequired();

        builder.Property(l => l.Quantity)
            .IsRequired();
    }
}
