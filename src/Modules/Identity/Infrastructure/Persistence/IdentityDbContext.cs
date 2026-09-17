using Microsoft.EntityFrameworkCore;

namespace WhatsAppMonitorAssistant.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>identity</c> schema and its own migration history. docs/TECHNICAL.md defines no
/// identity tables yet, so the context owns the schema only; admin login tables arrive with the
/// Identity/admin ticket.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public const string MigrationsHistoryTableName = "__ef_migrations";

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
