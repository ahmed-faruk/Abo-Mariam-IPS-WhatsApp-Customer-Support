using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence.Configurations;

internal sealed class ConversationStateConfiguration : IEntityTypeConfiguration<ConversationState>
{
    public void Configure(EntityTypeBuilder<ConversationState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("conversation_state");

        builder.HasKey(state => state.ConversationId);
        builder.Property(state => state.ConversationId).HasColumnName("conversation_id").ValueGeneratedNever();

        builder.Property(state => state.StateJson)
            .HasColumnName("state_json")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(state => state.ExpiresAt).HasColumnName("expires_at");
        builder.Property(state => state.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");

        builder.HasOne(state => state.Conversation)
            .WithOne(conversation => conversation.State)
            .HasForeignKey<ConversationState>(state => state.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
