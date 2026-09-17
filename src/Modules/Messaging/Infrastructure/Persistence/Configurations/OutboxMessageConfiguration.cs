using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox_message", table =>
        {
            table.HasCheckConstraint("ck_outbox_sender", "sender IN ('AI','Agent','System')");
            table.HasCheckConstraint(
                "ck_outbox_status",
                "delivery_status IN ('Pending','Claimed','Sent','Failed','DeadLettered')");
            table.HasCheckConstraint("ck_outbox_body_hash", "body_hash = sha256(body::bytea)");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(message => message.ConversationId).HasColumnName("conversation_id");
        builder.Property(message => message.CustomerExternalId).HasColumnName("customer_external_id").IsRequired();
        builder.Property(message => message.CorrelationId).HasColumnName("correlation_id").IsRequired();

        builder.Property(message => message.Sender)
            .HasColumnName("sender")
            .HasDefaultValue(OutboxSenders.Ai);

        builder.Property(message => message.Body).HasColumnName("body").IsRequired();
        builder.Property(message => message.BodyHash).HasColumnName("body_hash").IsRequired();

        builder.Property(message => message.ProviderMessageId).HasColumnName("provider_message_id");
        builder.HasIndex(message => message.ProviderMessageId).IsUnique();

        builder.Property(message => message.DeliveryStatus)
            .HasColumnName("delivery_status")
            .HasDefaultValue(OutboxDeliveryStatuses.Pending);

        builder.Property(message => message.PartitionKey).HasColumnName("partition_key").IsRequired();
        builder.Property(message => message.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(message => message.MaxAttempts).HasColumnName("max_attempts").HasDefaultValue(5);
        builder.Property(message => message.RunAfter).HasColumnName("run_after").HasDefaultValueSql("now()");
        builder.Property(message => message.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(message => message.SentAt).HasColumnName("sent_at");
        builder.Property(message => message.LastError).HasColumnName("last_error");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(message => new { message.RunAfter, message.Id })
            .HasDatabaseName("ix_outbox_claim")
            .HasFilter("delivery_status = 'Pending'");
    }
}
