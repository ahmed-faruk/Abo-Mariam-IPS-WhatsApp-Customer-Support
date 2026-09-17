using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("customer");

        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(customer => customer.WhatsappNumber).HasColumnName("whatsapp_number").IsRequired();
        builder.HasIndex(customer => customer.WhatsappNumber).IsUnique();

        builder.Property(customer => customer.DisplayName).HasColumnName("display_name");
        builder.Property(customer => customer.FirstSeenAt).HasColumnName("first_seen_at").HasDefaultValueSql("now()");
        builder.Property(customer => customer.LastSeenAt).HasColumnName("last_seen_at").HasDefaultValueSql("now()");
    }
}
