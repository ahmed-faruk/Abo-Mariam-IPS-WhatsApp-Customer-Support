using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>storefront</c> schema and its own migration history. No other module may inject
/// this context; cross-module callers use the Storefront Contracts.
/// </summary>
public sealed class StorefrontDbContext(DbContextOptions<StorefrontDbContext> options) : DbContext(options)
{
    public const string Schema = "storefront";

    public const string MigrationsHistoryTableName = "__ef_migrations";

    public DbSet<BusinessInfo> BusinessInfo => Set<BusinessInfo>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StorefrontDbContext).Assembly);
    }
}
