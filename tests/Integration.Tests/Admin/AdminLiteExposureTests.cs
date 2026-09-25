using System.Net;
using Microsoft.Extensions.Options;
using WhatsAppMonitorAssistant.Integration.Tests.Persistence;

namespace WhatsAppMonitorAssistant.Integration.Tests.Admin;

/// <summary>
/// Focused Host/security integration test for the two-listener exposure rule of docs/TECHNICAL.md
/// section 36.2 — not a full application E2E test. Admin Lite answers only on the admin listener; on
/// the public listener, the one the tunnel exposes, every /admin request is not found.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AdminLiteExposureTests(PostgresContainerFixture postgres)
{
    [Theory]
    [InlineData("/admin/business-info")]
    [InlineData("/ADMIN/Business-Info")]
    [InlineData("/admin")]
    [InlineData("/admin/")]
    [InlineData("/admin/catalog")]
    [InlineData("/admin/conversations")]
    public async Task E1_admin_paths_are_not_found_on_the_public_listener(string path)
    {
        await using var factory = await StartAsync();
        using var client = factory.PublicClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task E2_the_admin_listener_serves_admin_lite()
    {
        await using var factory = await StartAsync();
        using var client = factory.AdminClient();

        using var response = await client.GetAsync("/admin/business-info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("WorkingHours", html, StringComparison.Ordinal);
        Assert.Contains("ReturnExchangePolicy", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task E3_a_post_to_admin_on_the_public_listener_is_not_found()
    {
        await using var factory = await StartAsync();
        using var client = factory.PublicClient();

        using var response = await client.PostAsync(
            "/admin/business-info?handler=WorkingHours",
            new FormUrlEncodedContent([new("answerAr", "public attempt")]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task E4_liveness_answers_on_both_listeners()
    {
        await using var factory = await StartAsync();
        using var publicClient = factory.PublicClient();
        using var adminClient = factory.AdminClient();

        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task E5_without_a_configured_admin_port_admin_is_not_found_anywhere()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = new AdminLiteHostFactory(connectionString, configureAdminPort: false);
        factory.StartServer();
        using var client = factory.AdminClient();

        using var response = await client.GetAsync("/admin/business-info");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("80")]
    [InlineData("70000")]
    public async Task E6_an_invalid_admin_port_stops_the_host_from_starting(string port)
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = new AdminLiteHostFactory(connectionString, adminPortOverride: port);

        var exception = Assert.ThrowsAny<Exception>(factory.StartServer);

        Assert.Contains(
            "AdminLite:Port must be unset or between 1024 and 65535",
            (exception as OptionsValidationException ?? exception.InnerException as OptionsValidationException)?.Message
                ?? exception.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public-port", "is also used by Kestrel:Endpoints:Public:Url")]
    [InlineData("wildcard", "Kestrel:Endpoints:Admin:Url must be a loopback http URL")]
    [InlineData("lan", "Kestrel:Endpoints:Admin:Url must be a loopback http URL")]
    [InlineData("missing", "AdminLite:Port requires Kestrel:Endpoints:Admin:Url")]
    public async Task E7_an_admin_listener_that_is_not_a_distinct_loopback_endpoint_stops_the_host(string setup, string message)
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        await using var factory = setup switch
        {
            // The admin port is the public listener's port, which is the one the tunnel exposes.
            "public-port" => new AdminLiteHostFactory(connectionString, adminPortEqualsPublicPort: true),
            "wildcard" => new AdminLiteHostFactory(connectionString, adminUrlOverride: port => $"http://0.0.0.0:{port}"),
            "lan" => new AdminLiteHostFactory(connectionString, adminUrlOverride: port => $"http://192.168.1.10:{port}"),
            _ => new AdminLiteHostFactory(connectionString, adminUrlOverride: _ => null),
        };

        var exception = Assert.ThrowsAny<Exception>(factory.StartServer);

        Assert.Contains(message, exception.ToString(), StringComparison.Ordinal);
    }

    private async Task<AdminLiteHostFactory> StartAsync()
    {
        var factory = new AdminLiteHostFactory(await postgres.CreateMigratedDatabaseAsync());
        factory.StartServer();

        return factory;
    }
}
