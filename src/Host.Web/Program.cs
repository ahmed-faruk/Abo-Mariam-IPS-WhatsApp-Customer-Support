using WhatsAppMonitorAssistant.Host.Web.Admin;
using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Host.Web.Health;
using WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationComposition(builder.Configuration);
builder.Services.AddAdminLite(builder.Configuration);

var app = builder.Build();

// The Admin Lite guard runs before every endpoint, so /admin is not found on any port but the admin one.
app.UseAdminLitePortGuard();

app.MapHealthEndpoints();
app.MapWhatsAppWebhookEndpoints();
app.MapAdminLite();

await app.RunAsync();

public partial class Program
{
}
