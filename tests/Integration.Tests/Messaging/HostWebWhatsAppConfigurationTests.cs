using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Messaging;

/// <summary>
/// The real composition root must refuse a WhatsApp transport budget that cannot fit inside the
/// Outbox claim lease. Options resolution is where the host learns its configuration, so a host that
/// would send on an unenforceable budget stops there instead of being silently capped.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class HostWebWhatsAppConfigurationTests(PostgresContainerFixture postgres)
{
    private const string VerifyToken = "test-verify-token";
    private const string AppSecret = "test-app-secret";
    private const string AccessToken = "test-access-token";
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=5432;Database=host_web_configuration;Username=monitor_app";

    [Fact]
    public async Task The_composition_root_rejects_a_transport_timeout_that_cannot_fit_the_claim_lease()
    {
        // A real migrated database proves the composition is the production one, not a test host.
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var configuration = ConfigurationFor(connectionString);
        configuration["WhatsApp:TimeoutSeconds"] = "9999";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(configuration);

        await using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value);

        Assert.Contains(nameof(WhatsAppOptions.TimeoutSeconds), exception.Message, StringComparison.Ordinal);
        Assert.Contains("ClaimLeaseDuration", exception.Message, StringComparison.Ordinal);

        // A configuration failure must never echo a secret back into logs or an operator console.
        foreach (var secret in new[] { VerifyToken, AppSecret, AccessToken })
        {
            Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_composition_root_accepts_the_default_transport_budget()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(ConfigurationFor(UnreachableConnectionString));

        using var provider = services.BuildServiceProvider();

        // Nothing is overridden, so the documented defaults must resolve without a validation failure.
        Assert.Equal(20, provider.GetRequiredService<IOptions<WhatsAppOptions>>().Value.TimeoutSeconds);
    }

    private static ConfigurationManager ConfigurationFor(string connectionString) =>
        new()
        {
            [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
            ["Catalog:Search:SizeToleranceInches"] = "0.5",
            ["Catalog:Search:SoftBudgetTolerance"] = "0.1",
            ["Ai:Provider"] = "Ollama",
            ["Ai:BaseUrl"] = "http://127.0.0.1:11434",
            ["Ai:Model"] = "qwen3.5:2b-q4_K_M",
            ["Ai:TimeoutSeconds"] = "20",
            ["Ai:Temperature"] = "0",
            ["Ai:ContextTokens"] = "4096",
            ["WhatsApp:ApiVersion"] = "v23.0",
            ["WhatsApp:PhoneNumberId"] = "123",
            ["WhatsApp:VerifyToken"] = VerifyToken,
            ["WhatsApp:AppSecret"] = AppSecret,
            ["WhatsApp:AccessToken"] = AccessToken,
        };
}
