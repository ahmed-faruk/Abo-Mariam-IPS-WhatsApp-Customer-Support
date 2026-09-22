using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("conversation", table =>
            table.HasCheckConstraint("ck_conversation_mode", "mode IN ('AI','Human','Closed')"));

        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(conversation => conversation.CustomerId).HasColumnName("customer_id");

        builder.Property(conversation => conversation.Mode)
            .HasColumnName("mode")
            .HasDefaultValue(ConversationModes.Ai);

        builder.Property(conversation => conversation.ModeRevision)
            .HasColumnName("mode_revision")
            .HasDefaultValue(0L);

        builder.Property(conversation => conversation.WindowExpiresAt).HasColumnName("window_expires_at");
        builder.Property(conversation => conversation.StartedAt).HasColumnName("started_at").HasDefaultValueSql("now()");
        builder.Property(conversation => conversation.LastInboundAt).HasColumnName("last_inbound_at");
        builder.Property(conversation => conversation.LastOutboundAt).HasColumnName("last_outbound_at");
        builder.Property(conversation => conversation.ClosedAt).HasColumnName("closed_at");
        builder.Property(conversation => conversation.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(conversation => conversation.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasOne(conversation => conversation.Customer)
            .WithMany()
            .HasForeignKey(conversation => conversation.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(conversation => conversation.CustomerId).HasDatabaseName("ix_conversation_customer");

        // One open (non-closed) conversation per customer; closed history stays unlimited.
        builder.HasIndex(conversation => conversation.CustomerId)
            .IsUnique()
            .HasDatabaseName("ux_conversation_open_customer")
            .HasFilter("mode <> 'Closed'");
    }
}
