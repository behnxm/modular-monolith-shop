using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Ordering.Domain;

namespace Shop.Modules.Ordering.Infrastructure;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("order_idempotency_records", "ordering");

        builder.HasKey(r => new { r.CustomerId, r.Key });

        builder.Property(r => r.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Key)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.RequestHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(r => r.OrderId)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();
    }
}
