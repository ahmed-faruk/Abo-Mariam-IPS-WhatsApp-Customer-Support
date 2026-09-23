using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Host.Web.Health;
using WhatsAppMonitorAssistant.Modules.Messaging.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationComposition(builder.Configuration);

var app = builder.Build();

app.MapHealthEndpoints();
app.MapWhatsAppWebhookEndpoints();

await app.RunAsync();
