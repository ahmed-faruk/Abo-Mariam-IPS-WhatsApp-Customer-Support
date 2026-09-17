using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence.Configurations;

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inbox_message", table => table.HasCheckConstraint(
            "ck_inbox_status",
            "processing_status IN ('Pending','Claimed','Processed','Failed','DeadLettered')"));

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(message => message.EnvelopeId).HasColumnName("envelope_id");

        builder.Property(message => message.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .IsRequired();
        builder.HasIndex(message => message.ProviderMessageId).IsUnique();

        builder.Property(message => message.CustomerExternalId).HasColumnName("customer_external_id").IsRequired();
        builder.Property(message => message.ConversationId).HasColumnName("conversation_id");
        builder.Property(message => message.MessageType).HasColumnName("message_type").IsRequired();
        builder.Property(message => message.Body).HasColumnName("body");
        builder.Property(message => message.ProviderTimestamp).HasColumnName("provider_timestamp");

        builder.Property(message => message.ProcessingStatus)
            .HasColumnName("processing_status")
            .HasDefaultValue(InboxProcessingStatuses.Pending);

        builder.Property(message => message.PartitionKey).HasColumnName("partition_key").IsRequired();
        builder.Property(message => message.Attempts).HasColumnName("attempts").HasDefaultValue(0);
        builder.Property(message => message.RunAfter).HasColumnName("run_after").HasDefaultValueSql("now()");
        builder.Property(message => message.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(message => message.ProcessedAt).HasColumnName("processed_at");
        builder.Property(message => message.LastError).HasColumnName("last_error");
        builder.Property(message => message.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("now()");

        builder.HasOne(message => message.Envelope)
            .WithMany()
            .HasForeignKey(message => message.EnvelopeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(message => message.EnvelopeId).HasDatabaseName("ix_inbox_message_envelope");

        builder.HasIndex(message => new { message.RunAfter, message.Id })
            .HasDatabaseName("ix_inbox_claim")
            .HasFilter("processing_status = 'Pending'");
    }
}
