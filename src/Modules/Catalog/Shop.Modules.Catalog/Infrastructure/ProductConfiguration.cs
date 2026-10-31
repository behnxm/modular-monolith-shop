using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Modules.Catalog.Domain;

namespace Shop.Modules.Catalog.Infrastructure;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", "catalog");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku)
            .HasConversion(sku => sku.Value, value => Sku.Create(value).Value)
            .IsRequired()
            .HasMaxLength(32);

        builder.HasIndex(p => p.Sku)
            .IsUnique()
            .HasDatabaseName("ix_catalog_products_sku");

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(p => p.Price)
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(p => p.IsActive)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.Version)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(p => new { p.CreatedAt, p.Id })
            .HasDatabaseName("ix_catalog_products_keyset");
    }
}
