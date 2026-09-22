using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Modules.Catalog.Contracts;
using WhatsAppMonitorAssistant.Modules.Catalog.Features.SearchProducts;
using WhatsAppMonitorAssistant.Modules.Catalog.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;
using WhatsAppMonitorAssistant.Modules.Messaging.Infrastructure;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure;

namespace WhatsAppMonitorAssistant.Integration.Tests.Conversations;

/// <summary>
/// Builds the real inbound orchestration over a throwaway PostgreSQL database with the real Catalog,
/// Storefront and Messaging modules, and binds the production deterministic renderer. Only the AI is
/// replaced, so the renderer re-reads genuinely current prices, stock, active state and business values.
/// </summary>
internal sealed class ConversationRendererHost(ServiceProvider provider) : IAsyncDisposable
{
    /// <summary>The tolerances are test inputs, exactly like the Catalogue suite states them.</summary>
    private const decimal SizeToleranceInches = 0.4m;

    private const decimal SoftBudgetTolerance = 0.25m;

    public static ConversationRendererHost Start(
        string connectionString,
        Action<IServiceCollection> configureServices)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCatalogModule(connectionString, options =>
        {
            options.SizeToleranceInches = SizeToleranceInches;
            options.SoftBudgetTolerance = SoftBudgetTolerance;
        });
        services.AddStorefrontModule(connectionString);
        services.AddMessagingModule(connectionString);
        services.AddConversationsModule(connectionString);

        configureServices(services);

        return new ConversationRendererHost(services.BuildServiceProvider());
    }

    public AsyncServiceScope CreateScope() => provider.CreateAsyncScope();

    public ValueTask DisposeAsync() => provider.DisposeAsync();
}

/// <summary>Registers the one stub an otherwise real composition needs, and hands it back.</summary>
internal static class RealRendererDoubles
{
    /// <summary>
    /// Registers the stub AI and binds the production renderer built from the real module contracts.
    /// </summary>
    /// <param name="beforeRender">
    /// An optional hook that runs once, immediately before the first reply of the test is rendered. It is
    /// what a test uses to change a catalogue or Storefront fact exactly between the turn's preliminary
    /// decision and the revalidation the reply is really built from.
    /// </param>
    public static StubAiNluClient AddRealRenderer(
        this IServiceCollection services,
        NluAnalysisResult analysis,
        Func<CancellationToken, Task>? beforeRender = null)
    {
        var nlu = new StubAiNluClient(analysis);

        services.AddSingleton<TimeProvider>(new FixedClock(ConversationsTestDoubles.Now));
        services.AddSingleton<IAiNluClient>(nlu);
        services.AddScoped<IConversationRenderer>(provider =>
        {
            var renderer = new DeterministicConversationRenderer(
                provider.GetRequiredService<ICatalogSearch>(),
                provider.GetRequiredService<ICatalogProductDetails>(),
                provider.GetRequiredService<IStorefrontBusinessInfo>());

            return beforeRender is null ? renderer : new MutatingRenderer(renderer, beforeRender);
        });

        return nlu;
    }
}

/// <summary>
/// The production renderer, with a one-shot hook in front of it. It is the seam that lets a test change a
/// fact after the turn has already decided what to answer and before the reply re-reads the current facts.
/// </summary>
internal sealed class MutatingRenderer(
    IConversationRenderer inner,
    Func<CancellationToken, Task> beforeRender) : IConversationRenderer
{
    private int rendered;

    public async Task<ConversationRenderResult> RenderAsync(
        ConversationResponseIntent intent,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref rendered) == 1)
        {
            await beforeRender(cancellationToken);
        }

        return await inner.RenderAsync(intent, cancellationToken);
    }
}
