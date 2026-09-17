using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class ProductModelPortConfiguration : IEntityTypeConfiguration<ProductModelPort>
{
    public void Configure(EntityTypeBuilder<ProductModelPort> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_model_port", table =>
            table.HasCheckConstraint("ck_port_count", "count BETWEEN 1 AND 16"));

        builder.HasKey(port => port.Id);
        builder.Property(port => port.Id).UseIdentityAlwaysColumn();

        builder.Property(port => port.ProductModelId).HasColumnName("product_model_id");
        builder.Property(port => port.PortType).HasColumnName("port_type").IsRequired();
        builder.Property(port => port.Count).HasColumnName("count").HasDefaultValue(1);

        builder.HasIndex(port => new { port.ProductModelId, port.PortType })
            .IsUnique()
            .HasDatabaseName("uq_model_port");

        builder.HasIndex(port => port.ProductModelId).HasDatabaseName("ix_model_port_model");
    }
}
