using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Notifications.Domain;

namespace Shop.Modules.Notifications.Infrastructure;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", "notifications");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.CustomerId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(n => n.Kind)
            .HasConversion<string>()
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(n => n.Subject)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(n => n.CreatedAt)
            .IsRequired();

        builder.HasIndex(n => new { n.CustomerId, n.CreatedAt });
    }
}
