using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>conversations</c> schema and its own migration history. No other module may inject
/// this context; cross-module callers use the Conversations Contracts.
/// </summary>
public sealed class ConversationDbContext(DbContextOptions<ConversationDbContext> options) : DbContext(options)
{
    public const string Schema = "conversations";

    public const string MigrationsHistoryTableName = "__ef_migrations";

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<ConversationState> ConversationStates => Set<ConversationState>();

    /// <summary>Applies the shared Npgsql options used at runtime and at design time.</summary>
    public static void Configure(DbContextOptionsBuilder optionsBuilder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTableName, Schema));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConversationDbContext).Assembly);
    }
}
