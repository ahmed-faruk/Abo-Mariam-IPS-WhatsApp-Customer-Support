using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Messaging.Contracts;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// The real composition root must be able to build the inbound orchestration before the deterministic
/// renderer of Issue #12 exists. Until then the host runs the fail-closed renderer, which produces no
/// text, so the application starts and no customer-facing reply can be published by accident.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ConversationCompositionTests(PostgresContainerFixture postgres)
{
    [Fact]
    public async Task The_application_composition_resolves_orchestration_with_the_fail_closed_renderer()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        var configuration = new ConfigurationManager
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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(configuration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IProcessInboundTurn>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConversationModeControl>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboundMessageProcessor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboundMessageQueue>());

        var renderer = scope.ServiceProvider.GetRequiredService<IConversationRenderer>();

        Assert.IsType<FailClosedConversationRenderer>(renderer);

        // The fail-closed renderer is the documented behaviour of the host before Issue #12: it renders
        // nothing, which is what stops an outbound row from ever being created.
        var rendered = await renderer.RenderAsync(new ConversationResponseIntent
        {
            Kind = ConversationResponseKind.Greeting,
            ConversationId = 1,
            CustomerExternalId = "20100000001",
        });

        Assert.False(rendered.IsRendered);
        Assert.Null(rendered.Body);
    }
}
