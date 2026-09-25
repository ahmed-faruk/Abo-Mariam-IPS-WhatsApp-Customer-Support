using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;
using WhatsAppMonitorAssistant.Modules.Storefront.Contracts;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// The Admin Lite WorkingHours edit: it goes through the existing Storefront update contract, so the
/// customer-facing read observes the new stored value, and it is protected by antiforgery validation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminLiteBusinessInfoTests(PostgresContainerFixture postgres)
{
    private const string Page = "/admin/business-info";
    private const string NewHours = "مواعيدنا الجديدة: كل يوم من 12 الضهر لحد 11 بالليل.";

    [Fact]
    public async Task K1_editing_working_hours_is_observed_by_the_customer_read_path()
    {
        await using var factory = await StartSeededAsync();
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, "WorkingHours", [new("answerAr", NewHours)]);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(NewHours, await CustomerReadAsync(factory));
        Assert.Contains(NewHours, await client.GetStringAsync(Page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task K2_a_blank_answer_is_rejected_and_nothing_changes()
    {
        await using var factory = await StartSeededAsync();
        using var client = factory.AdminClient();

        using var response = await AdminForms.PostAsync(client, Page, "WorkingHours", [new("answerAr", "   ")]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApprovedHours, await CustomerReadAsync(factory));
    }

    [Fact]
    public async Task K3_a_post_without_an_antiforgery_token_is_rejected_and_nothing_changes()
    {
        await using var factory = await StartSeededAsync();
        using var client = factory.AdminClient();

        using var response = await client.PostAsync(
            $"{Page}?handler=WorkingHours",
            new FormUrlEncodedContent([new("answerAr", NewHours)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApprovedHours, await CustomerReadAsync(factory));
    }

    private static string ApprovedHours =>
        DemoDataset.BusinessInfo.Single(row => row.Key == BusinessInfoKeyNames.WorkingHours).AnswerAr;

    private async Task<AdminLiteHostFactory> StartSeededAsync()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        Assert.Equal(DemoCli.Success, await AdminForms.RunDemoOpsAsync(connectionString, "seed"));

        var factory = new AdminLiteHostFactory(connectionString);
        factory.StartServer();

        return factory;
    }

    private static async Task<string?> CustomerReadAsync(AdminLiteHostFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<IStorefrontBusinessInfo>()
            .GetByKeyAsync(BusinessInfoKeyNames.WorkingHours))?.AnswerAr;
    }
}
