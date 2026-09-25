using Microsoft.Extensions.Configuration;

namespace WhatsAppMonitorAssistant.Tools.DemoOps;

/// <summary>
/// The operator entry point. Configuration comes from environment variables only, so the tool reads
/// exactly the connection string and the controlled-demo tolerances the runbook exports.
/// </summary>
internal static class DemoOpsProgram
{
    public static Task<int> Main(string[] args) =>
        DemoCli.RunAsync(
            args,
            new ConfigurationBuilder().AddEnvironmentVariables().Build(),
            Console.Out,
            Console.Error);
}
