using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The real composition root must build the inbound orchestration with the deterministic renderer of
/// Issue #12 bound: the host that runs is the host that answers customers from current facts, and the
/// fail-closed renderer is only the seam a test binds deliberately.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationCompositionTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task The_application_composition_resolves_orchestration_with_the_deterministic_renderer()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(ConfigurationFor(connectionString));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConversationModeControl>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboundMessageProcessor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>());

        var renderer = scope.ServiceProvider.GetRequiredService<IConversationRenderer>();

        Assert.IsType<DeterministicConversationRenderer>(renderer);

        // The bound renderer writes application-owned text, and it reads no commercial fact to do it.
        var rendered = await renderer.RenderAsync(new ConversationResponseIntent
        {
            Kind = ConversationResponseKind.Greeting,
            ConversationId = 1,
            CustomerExternalId = "20100000001",
        });

        Assert.True(rendered.IsRendered);
        Assert.Empty(rendered.DisplayedCandidates);

        // The fail-closed renderer still exists as the explicit seam a test can bind.
        var failClosed = await new FailClosedConversationRenderer().RenderAsync(new ConversationResponseIntent
        {
            Kind = ConversationResponseKind.Greeting,
            ConversationId = 1,
            CustomerExternalId = "20100000001",
        });

        Assert.False(failClosed.IsRendered);
        Assert.Null(failClosed.Body);
    }

    [Fact]
    public async Task The_application_composition_has_a_valid_lifetime_graph_and_scopes_the_renderer()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(ConfigurationFor(connectionString));

        // A host that validates its own graph is what catches a captive dependency: the deterministic
        // renderer reads the scoped Catalog and Storefront contracts, so it must not be a singleton.
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var firstScope = provider.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IConversationRenderer>();

        Assert.IsType<DeterministicConversationRenderer>(first);

        await using var secondScope = provider.CreateAsyncScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IConversationRenderer>();

        // One renderer per scope, so two Inbox processing scopes never share a renderer or its readers.
        Assert.NotSame(first, second);
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
        };
}
