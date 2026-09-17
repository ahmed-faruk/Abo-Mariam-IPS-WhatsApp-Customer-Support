using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the Storefront context without starting the web host or connecting
/// to a database. Set <see cref="ConnectionStringVariableName"/> when a live connection is required.
/// </summary>
public sealed class StorefrontDesignTimeDbContextFactory : IDesignTimeDbContextFactory<StorefrontDbContext>
{
    public const string ConnectionStringVariableName = "MONITOR_DESIGN_TIME_CONNECTION";

    public StorefrontDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariableName)
            ?? "Host=127.0.0.1;Port=5432;Database=monitor_ai;Username=monitor_app";

        var optionsBuilder = new DbContextOptionsBuilder<StorefrontDbContext>();
        StorefrontDbContext.Configure(optionsBuilder, connectionString);

        return new StorefrontDbContext(optionsBuilder.Options);
    }
}
