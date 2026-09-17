using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Messaging.Domain;

namespace WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure.Persistence.Configurations;

internal sealed class WebhookEnvelopeConfiguration : IEntityTypeConfiguration<WebhookEnvelope>
{
    public void Configure(EntityTypeBuilder<WebhookEnvelope> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("webhook_envelope");

        builder.HasKey(envelope => envelope.Id);
        builder.Property(envelope => envelope.Id).UseIdentityAlwaysColumn();

        builder.Property(envelope => envelope.EnvelopeHash).HasColumnName("envelope_hash").IsRequired();
        builder.HasIndex(envelope => envelope.EnvelopeHash).IsUnique();

        builder.Property(envelope => envelope.RawBody).HasColumnName("raw_body").HasColumnType("jsonb").IsRequired();
        builder.Property(envelope => envelope.ReceivedAt).HasColumnName("received_at").HasDefaultValueSql("now()");
        builder.Property(envelope => envelope.ProcessedAt).HasColumnName("processed_at");
    }
}
