using ArchUnitNET.Loader;
using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;
using BuildingBlocksApplication = WhatsAppMonitorAssistant.BuildingBlocks.Application.ApplicationNamespaceMarker;
using BuildingBlocksDomain = WhatsAppMonitorAssistant.BuildingBlocks.Domain.DomainNamespaceMarker;
using BuildingBlocksMessaging = WhatsAppMonitorAssistant.BuildingBlocks.Messaging.MessagingNamespaceMarker;
using CatalogDomain = WhatsAppMonitorAssistant.Modules.Catalog.Domain.CatalogDomainMarker;
using ConversationsDomain = WhatsAppMonitorAssistant.Modules.Conversations.Domain.ConversationsDomainMarker;
using HostComposition = WhatsAppMonitorAssistant.Host.Web.Composition.CompositionRoot;
using IdentityDomain = WhatsAppMonitorAssistant.Modules.Identity.Domain.IdentityDomainMarker;
using IntelligenceDomain = WhatsAppMonitorAssistant.Modules.Intelligence.Domain.IntelligenceDomainMarker;
using MessagingDomain = WhatsAppMonitorAssistant.Modules.Messaging.Domain.MessagingDomainMarker;
using ReflectionAssembly = System.Reflection.Assembly;
using StorefrontDomain = WhatsAppMonitorAssistant.Modules.Storefront.Domain.StorefrontDomainMarker;

namespace WhatsAppMonitorAssistant.Architecture.Tests;

/// <summary>Loads the architectures under test: the production assemblies and the fixture assembly.</summary>
internal static class TestArchitectures
{
    public static ArchUnitArchitecture Production { get; } = new ArchLoader()
        .LoadAssemblies(ProductionAssemblies())
        .Build();

    public static ArchUnitArchitecture Fixtures { get; } = new ArchLoader()
        .LoadAssemblies(typeof(TestArchitectures).Assembly)
        .Build();

    private static ReflectionAssembly[] ProductionAssemblies() =>
    [
        typeof(HostComposition).Assembly,
        typeof(BuildingBlocksDomain).Assembly,
        typeof(BuildingBlocksApplication).Assembly,
        typeof(BuildingBlocksMessaging).Assembly,
        typeof(CatalogDomain).Assembly,
        typeof(ConversationsDomain).Assembly,
        typeof(IdentityDomain).Assembly,
        typeof(IntelligenceDomain).Assembly,
        typeof(MessagingDomain).Assembly,
        typeof(StorefrontDomain).Assembly,
    ];
}
