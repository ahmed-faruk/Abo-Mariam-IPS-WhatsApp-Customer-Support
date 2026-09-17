using Microsoft.EntityFrameworkCore;
using WhatsAppMonitorAssistant.Modules.Catalog.Domain;

namespace WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// Owns the <c>catalog</c> schema and its own migration history. No other module may inject
/// this context; cross-module callers use the Catalog Contracts.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public const string MigrationsHistoryTableName = "__ef_migrations";

    public DbSet<ProductModel> ProductModels => Set<ProductModel>();

    public DbSet<ProductModelPort> ProductModelPorts => Set<ProductModelPort>();

    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    public DbSet<CatalogAuditLog> AuditLog => Set<CatalogAuditLog>();

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
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
