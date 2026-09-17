using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class CatalogAuditLogConfiguration : IEntityTypeConfiguration<CatalogAuditLog>
{
    public void Configure(EntityTypeBuilder<CatalogAuditLog> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("audit_log");

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).UseIdentityAlwaysColumn();

        builder.Property(entry => entry.EntityType).HasColumnName("entity_type").IsRequired();
        builder.Property(entry => entry.EntityId).HasColumnName("entity_id");
        builder.Property(entry => entry.Action).HasColumnName("action").IsRequired();
        builder.Property(entry => entry.OldJson).HasColumnName("old_json").HasColumnType("jsonb");
        builder.Property(entry => entry.NewJson).HasColumnName("new_json").HasColumnType("jsonb");
        builder.Property(entry => entry.UserId).HasColumnName("user_id");
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
    }
}
