using System.Text.Json;
using WhatsAppMonitorAssistant.Modules.Intelligence.Contracts;

namespace WhatsAppMonitorAssistant.Modules.Intelligence.Domain;

/// <summary>
/// Validates one structured-NLU reply from a model against the frozen contract of
/// docs/TECHNICAL.md sections 8.3 and 9. Deserialization alone is not enough, so the validator checks
/// the JSON shape, the canonical vocabularies, the budget semantics and the null/empty-collection
/// convention, and rejects any key the documented contract does not contain — including the
/// commercial facts such as price or stock that the model must never author.
/// </summary>
/// <remarks>
/// Problems are path-based and never repeat the model's values, so a problem can be shown to an
/// operator without leaking model-authored commercial text. Only a received reply is validated; a
/// transport failure never reaches this type.
/// </remarks>
public static class NluReplyValidator
{
    /// <summary>
    /// The placeholder strings the frozen prompt explicitly forbids for an absent value. A reply that
    /// uses one of them said nothing, so it is an invalid reply rather than a stated fact.
    /// </summary>
    private static readonly string[] PlaceholderSentinels = ["unknown", "n/a", "default"];

    /// <summary>Validates one model reply.</summary>
    public static NluReplyValidation Validate(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return NluReplyValidation.Invalid(["$: the reply is empty"]);
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return NluReplyValidation.Invalid(["$: the reply is not valid JSON"]);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return NluReplyValidation.Invalid(["$: the reply must be a JSON object"]);
            }

            var problems = new List<string>();

            CollectUndocumentedFields(root, problems);
            CollectMissingRequiredFields(root, problems);

            var intent = ReadIntent(root, problems);
            var brand = ReadOptionalText(root, "brand", problems);
            var modelCode = ReadOptionalText(root, "modelCode", problems);
            var sizeInches = ReadOptionalNumber(root, "sizeInches", problems);
            var panel = ReadOptionalText(root, "panel", problems);
            var resolution = ReadOptionalText(root, "resolution", problems);
            var minRefreshRate = ReadOptionalInteger(root, "minRefreshRate", problems);
            var requiredPorts = ReadTextArray(root, "requiredPorts", problems);
            var grades = ReadTextArray(root, "grades", problems);
            var budgetType = ReadBudgetType(root, problems);
            var budgetTarget = ReadOptionalNumber(root, "budgetTarget", problems);
            var budgetMin = ReadOptionalNumber(root, "budgetMin", problems);
            var budgetMax = ReadOptionalNumber(root, "budgetMax", problems);
            var useCase = ReadOptionalText(root, "useCase", problems);
            var reference = ReadOptionalText(root, "reference", problems);

            if (sizeInches is <= 0)
            {
                problems.Add("$.sizeInches: the requested size must be greater than zero");
            }

            if (minRefreshRate is <= 0)
            {
                problems.Add("$.minRefreshRate: a refresh rate must be greater than zero");
            }

            if (budgetType is { } statedBudget)
            {
                problems.AddRange(NluBudgetRules.Validate(statedBudget, budgetTarget, budgetMin, budgetMax));
            }

            if (problems.Count > 0 || intent is null || budgetType is null)
            {
                return NluReplyValidation.Invalid(
                    problems.Count > 0
                        ? problems
                        : ["$: the reply did not satisfy the NLU contract"]);
            }

            return NluReplyValidation.Valid(new NluInterpretation
            {
                Intent = intent.Value,
                Brand = brand,
                ModelCode = modelCode,
                SizeInches = sizeInches,
                Panel = panel,
                Resolution = resolution,
                MinRefreshRate = minRefreshRate,
                RequiredPorts = requiredPorts,
                Grades = grades,
                BudgetType = budgetType.Value,
                BudgetTarget = budgetTarget,
                BudgetMin = budgetMin,
                BudgetMax = budgetMax,
                UseCase = useCase,
                Reference = reference,
            });
        }
    }

    private static void CollectUndocumentedFields(JsonElement root, List<string> problems)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!NluContract.Fields.Contains(property.Name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"$.{property.Name}: is not a field of the documented NLU contract, so it cannot "
                    + "become structured output");
            }
        }
    }

    private static void CollectMissingRequiredFields(JsonElement root, List<string> problems)
    {
        foreach (var required in NluContract.RequiredFields)
        {
            if (!root.TryGetProperty(required, out _))
            {
                problems.Add($"$.{required}: is required by the NLU contract");
            }
        }
    }

    private static NluIntent? ReadIntent(JsonElement root, List<string> problems)
    {
        if (ReadRequiredText(root, "intent", problems) is not { } intentName)
        {
            return null;
        }

        if (!Enum.TryParse<NluIntent>(intentName, ignoreCase: false, out var intent)
            || !string.Equals(intent.ToString(), intentName, StringComparison.Ordinal))
        {
            problems.Add(
                "$.intent: is not one of the documented intent names; use the canonical spelling, "
                + "for example ProductSearch rather than an alias");

            return null;
        }

        return intent;
    }

    private static NluBudgetType? ReadBudgetType(JsonElement root, List<string> problems)
    {
        if (ReadRequiredText(root, "budgetType", problems) is not { } budgetName)
        {
            return null;
        }

        if (!Enum.TryParse<NluBudgetType>(budgetName, ignoreCase: false, out var budgetType)
            || !string.Equals(budgetType.ToString(), budgetName, StringComparison.Ordinal))
        {
            problems.Add("$.budgetType: is not one of None, Soft, Hard or Range");

            return null;
        }

        return budgetType;
    }

    private static string? ReadRequiredText(JsonElement root, string field, List<string> problems)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            // The missing-required pass already reported the absent field.
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            problems.Add($"$.{field}: must be a string");

            return null;
        }

        var value = (element.GetString() ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            problems.Add($"$.{field}: must not be blank");

            return null;
        }

        return value;
    }

    private static string? ReadOptionalText(JsonElement root, string field, List<string> problems)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            problems.Add($"$.{field}: must be a string or null");

            return null;
        }

        var value = element.GetString() ?? string.Empty;
        var trimmed = value.Trim();

        if (trimmed.Length == 0 || IsPlaceholderSentinel(trimmed))
        {
            problems.Add(
                $"$.{field}: must be null when the customer did not provide it, never an empty string or a "
                + "placeholder such as \"unknown\", \"N/A\" or \"default\"");

            return null;
        }

        return trimmed;
    }

    private static bool IsPlaceholderSentinel(string value) =>
        PlaceholderSentinels.Any(sentinel => string.Equals(value, sentinel, StringComparison.OrdinalIgnoreCase));

    private static decimal? ReadOptionalNumber(JsonElement root, string field, List<string> problems)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var value))
        {
            problems.Add($"$.{field}: must be a number or null");

            return null;
        }

        return value;
    }

    private static int? ReadOptionalInteger(JsonElement root, string field, List<string> problems)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            problems.Add($"$.{field}: must be a whole number or null");

            return null;
        }

        return value;
    }

    private static string[] ReadTextArray(JsonElement root, string field, List<string> problems)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            problems.Add(
                $"$.{field}: must be an array, using an empty array when the customer provided nothing");

            return [];
        }

        var values = new List<string>();

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                problems.Add($"$.{field}: every member must be a string");

                continue;
            }

            var value = (item.GetString() ?? string.Empty).Trim();

            if (value.Length == 0)
            {
                problems.Add($"$.{field}: members must not be blank");

                continue;
            }

            values.Add(value);
        }

        return [.. values];
    }
}
