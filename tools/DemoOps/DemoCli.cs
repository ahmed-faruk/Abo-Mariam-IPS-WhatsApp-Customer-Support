using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WhatsAppMonitorAssistant.Host.Web.Composition;

namespace WhatsAppMonitorAssistant.Tools.DemoOps;

/// <summary>
/// The controlled-demo operator tool of docs/TECHNICAL.md section 36.5: <c>seed</c>, <c>reset</c> and
/// <c>verify</c>. It builds the real application composition plus the demo-data composition, never
/// starts a host, and reports its outcome through a fixed exit code.
/// </summary>
public static partial class DemoCli
{
    public const int Success = 0;

    public const int UnexpectedError = 1;

    public const int ResetRefused = 2;

    public const int VerifyFailed = 3;

    public const int UsageError = 64;

    public const string UsageText = """
        Usage:
          DemoOps seed
          DemoOps reset --customer <wa_id> [--customer <wa_id> ...]
          DemoOps verify --customer <wa_id> [--customer <wa_id> ...]

        <wa_id> is the customer's WhatsApp number: 8 to 15 digits, no '+'.
        Configuration (environment): ConnectionStrings__DefaultConnection,
        Catalog__Search__SizeToleranceInches, Catalog__Search__SoftBudgetTolerance.
        Exit codes: 0 success, 1 unexpected error, 2 reset refused, 3 verify failed, 64 usage error.
        """;

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        IConfiguration configuration,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (!TryParse(args, out var command, out var customers, out var problem))
        {
            error.WriteLine($"USAGE ERROR: {problem}");
            error.WriteLine(UsageText);

            return UsageError;
        }

        try
        {
            await using var services = BuildServices(configuration);
            var operations = new DemoOperations(services, output);

            return command switch
            {
                "seed" => await operations.SeedAsync(cancellationToken),
                "reset" => await operations.ResetAsync(customers, cancellationToken),
                _ => await operations.VerifyAsync(customers, cancellationToken),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            error.WriteLine($"ERROR: {exception.GetType().Name}: {exception.Message}");

            return UnexpectedError;
        }
    }

    private static ServiceProvider BuildServices(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationComposition(configuration);
        services.AddDemoDataOperations();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out string command,
        out IReadOnlyList<string> customers,
        out string problem)
    {
        command = args.Count > 0 ? args[0] : string.Empty;
        customers = [];
        problem = string.Empty;

        if (command is not ("seed" or "reset" or "verify"))
        {
            problem = args.Count == 0 ? "no command was given." : $"unknown command '{command}'.";

            return false;
        }

        if (command == "seed")
        {
            if (args.Count != 1)
            {
                problem = "seed takes no arguments.";

                return false;
            }

            return true;
        }

        var parsed = new List<string>();

        for (var index = 1; index < args.Count; index += 2)
        {
            if (args[index] != "--customer" || index + 1 >= args.Count)
            {
                problem = $"expected '--customer <wa_id>' but found '{args[index]}'.";

                return false;
            }

            var customer = args[index + 1];

            if (!CustomerIdPattern().IsMatch(customer))
            {
                problem = $"'{customer}' is not a WhatsApp number of 8 to 15 digits.";

                return false;
            }

            if (parsed.Contains(customer, StringComparer.Ordinal))
            {
                problem = $"the customer '{customer}' is given twice.";

                return false;
            }

            parsed.Add(customer);
        }

        if (parsed.Count == 0)
        {
            problem = $"{command} needs at least one '--customer <wa_id>'.";

            return false;
        }

        customers = parsed;

        return true;
    }

    [GeneratedRegex(@"^[0-9]{8,15}\z", RegexOptions.CultureInvariant)]
    private static partial Regex CustomerIdPattern();
}
