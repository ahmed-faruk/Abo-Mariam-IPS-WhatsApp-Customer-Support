using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductModelConfiguration : IEntityTypeConfiguration<ProductModel>
{
    public void Configure(EntityTypeBuilder<ProductModel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_model", table =>
        {
            table.HasCheckConstraint("ck_model_size", "size_inches BETWEEN 10 AND 60");
            table.HasCheckConstraint("ck_model_panel", "panel_type IN ('IPS','TN','VA','OLED','Other')");
            table.HasCheckConstraint("ck_model_resolution", "resolution_width > 0 AND resolution_height > 0");
            table.HasCheckConstraint("ck_model_refresh", "refresh_rate BETWEEN 24 AND 500");
        });

        builder.HasKey(model => model.Id);
        builder.Property(model => model.Id).UseIdentityAlwaysColumn();

        builder.Property(model => model.ModelCode).HasColumnName("model_code").IsRequired();
        builder.HasIndex(model => model.ModelCode).IsUnique();

        builder.Property(model => model.Brand).HasColumnName("brand").IsRequired();
        builder.Property(model => model.Model).HasColumnName("model").IsRequired();
        builder.Property(model => model.DisplayName).HasColumnName("display_name").IsRequired();

        builder.Property(model => model.SizeInches).HasColumnName("size_inches").HasPrecision(4, 1);
        builder.Property(model => model.PanelType).HasColumnName("panel_type").IsRequired();
        builder.Property(model => model.ResolutionWidth).HasColumnName("resolution_width");
        builder.Property(model => model.ResolutionHeight).HasColumnName("resolution_height");
        builder.Property(model => model.RefreshRate).HasColumnName("refresh_rate");

        builder.Property(model => model.Description).HasColumnName("description");
        builder.Property(model => model.SearchTags)
            .HasColumnName("search_tags")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'::text[]");

        builder.Property(model => model.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(model => model.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(model => model.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasMany(model => model.Ports)
            .WithOne(port => port.ProductModel)
            .HasForeignKey(port => port.ProductModelId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(model => model.Variants)
            .WithOne(variant => variant.ProductModel)
            .HasForeignKey(variant => variant.ProductModelId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
