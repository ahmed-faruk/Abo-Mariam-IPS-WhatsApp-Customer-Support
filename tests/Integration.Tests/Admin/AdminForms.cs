using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Tools.DemoOps;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// Submits Admin Lite forms the way a browser does: the page is read first, so the antiforgery cookie
/// and the matching hidden token of that page are sent back with the post.
/// </summary>
internal static partial class AdminForms
{
    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string page,
        string handler,
        IEnumerable<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(page);
        var token = AntiforgeryToken().Match(html);

        Assert.True(token.Success, $"The page {page} rendered no antiforgery token.");

        return await client.PostAsync(
            $"{page}?handler={handler}",
            new FormUrlEncodedContent([.. fields, new("__RequestVerificationToken", token.Groups["token"].Value)]));
    }

    /// <summary>Runs the DemoOps tool against a test database with the controlled-demo tolerances.</summary>
    public static async Task<int> RunDemoOpsAsync(string connectionString, params string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{CompositionRoot.ConnectionStringName}"] = connectionString,
                ["Catalog:Search:SizeToleranceInches"] = "0.5",
                ["Catalog:Search:SoftBudgetTolerance"] = "0.10",
            })
            .Build();

        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        return await DemoCli.RunAsync(args, configuration, output, error);
    }

    [GeneratedRegex("""name="__RequestVerificationToken" type="hidden" value="(?<token>[^"]+)" """, RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryToken();
}
