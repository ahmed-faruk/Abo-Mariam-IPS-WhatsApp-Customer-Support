using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_variant", table =>
        {
            table.HasCheckConstraint("ck_variant_grade", "grade IN ('A','B','C')");
            table.HasCheckConstraint("ck_variant_price", "selling_price >= 0");
            table.HasCheckConstraint("ck_variant_quantity", "quantity >= 0");
            table.HasCheckConstraint("ck_variant_warranty", "warranty_days >= 0");
        });

        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(variant => variant.ProductModelId).HasColumnName("product_model_id");

        builder.Property(variant => variant.Sku).HasColumnName("sku").IsRequired();
        builder.HasIndex(variant => variant.Sku).IsUnique();

        builder.Property(variant => variant.Grade).HasColumnName("grade").IsRequired();
        builder.Property(variant => variant.SellingPrice).HasColumnName("selling_price").HasPrecision(12, 2);
        builder.Property(variant => variant.Quantity).HasColumnName("quantity").HasDefaultValue(0);
        builder.Property(variant => variant.WarrantyDays).HasColumnName("warranty_days").HasDefaultValue(0);
        builder.Property(variant => variant.WarrantyNotes).HasColumnName("warranty_notes");
        builder.Property(variant => variant.CosmeticNotes).HasColumnName("cosmetic_notes");
        builder.Property(variant => variant.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(variant => variant.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(variant => variant.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasIndex(variant => new { variant.ProductModelId, variant.Grade })
            .IsUnique()
            .HasDatabaseName("uq_variant_model_grade");

        builder.HasIndex(variant => variant.ProductModelId).HasDatabaseName("ix_variant_model");
    }
}
