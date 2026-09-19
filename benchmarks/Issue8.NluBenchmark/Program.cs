using WhatsAppMonitorAssistant.Benchmarks.Nlu;

var services = BenchmarkServices.ForConsole();

return await BenchmarkEntryPoint.RunAsync(args, services, CancellationToken.None);
