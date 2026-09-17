using WhatsAppMonitorAssistant.Host.Web.Composition;
using WhatsAppMonitorAssistant.Host.Web.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplicationComposition(builder.Configuration);

var app = builder.Build();

app.MapHealthEndpoints();

await app.RunAsync();
