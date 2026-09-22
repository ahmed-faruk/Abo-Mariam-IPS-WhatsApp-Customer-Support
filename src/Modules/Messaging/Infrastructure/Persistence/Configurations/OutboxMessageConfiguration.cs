using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;
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
            // The stored hash is the one the application computes over the literal UTF-8 body bytes.
            // "body::bytea" would instead parse backslash and "\x" escape notation inside the text,
            // so a reply that merely contains such characters would fail to enqueue.
            table.HasCheckConstraint("ck_outbox_body_hash", "body_hash = sha256(convert_to(body, 'UTF8'))");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(message => message.ConversationId).HasColumnName("conversation_id");
        builder.Property(message => message.CustomerExternalId).HasColumnName("customer_external_id").IsRequired();
        builder.Property(message => message.CorrelationId).HasColumnName("correlation_id").IsRequired();

        // One durable reply per inbound turn: the correlation is the inbound provider message id, which
        // is already unique in the Inbox, so a globally unique correlation is the smallest guarantee
        // that makes the Outbox enqueue idempotent instead of duplicating a retried turn's reply.
        builder.HasIndex(message => message.CorrelationId)
            .IsUnique()
            .HasDatabaseName("ux_outbox_correlation_id");

        builder.Property(message => message.Sender)
            .HasColumnName("sender")
            .HasDefaultValue(OutboxSenders.Ai);

        builder.Property(message => message.Body).HasColumnName("body").IsRequired();
        builder.Property(message => message.BodyHash).HasColumnName("body_hash").IsRequired();

        // The metadata next to an immutable reply is bounded by the database itself, so a caller cannot
        // turn the Outbox row into an unbounded payload store. Existing rows simply have none.
        builder.Property(message => message.ApplicationMetadata)
            .HasColumnName("application_metadata")
            .HasMaxLength(OutboundMessageRequest.MaxApplicationMetadataLength);

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
        builder.Property(message => message.ClaimToken).HasColumnName("claim_token");
        builder.Property(message => message.ClaimExpiresAt).HasColumnName("claim_expires_at");
        builder.Property(message => message.SentAt).HasColumnName("sent_at");
        builder.Property(message => message.LastError).HasColumnName("last_error");
        builder.Property(message => message.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");

        builder.HasIndex(message => new { message.RunAfter, message.Id })
            .HasDatabaseName("ix_outbox_claim")
            .HasFilter("delivery_status = 'Pending'");
    }
}
