using System.Globalization;
using System.Text.Json;

namespace WhatsAppMonitorAssistant.Benchmarks.Nlu;

/// <summary>
/// Validates a value against the benchmark schema document. Only the JSON Schema keywords
/// the committed schema actually uses are implemented; an unknown keyword fails loudly so
/// schema and validator cannot silently drift apart.
/// </summary>
public sealed class JsonSchemaValidator
{
    private static readonly HashSet<string> SupportedKeywords = new(StringComparer.Ordinal)
    {
        "$schema",
        "$id",
        "title",
        "description",
        "type",
        "properties",
        "required",
        "items",
        "enum",
        "additionalProperties",
    };

    private readonly JsonElement _schema;

    public JsonSchemaValidator(JsonElement schema)
    {
        _schema = schema;
        AssertSchemaIsSupported(schema, "$");
    }

    public static JsonSchemaValidator FromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new BenchmarkDataException($"Schema not found at {path}.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return new JsonSchemaValidator(document.RootElement.Clone());
        }
        catch (JsonException exception)
        {
            throw new BenchmarkDataException($"Schema {path} is not valid JSON.", exception);
        }
    }

    /// <summary>Validates JSON text. Empty result means the payload matches the schema.</summary>
    public IReadOnlyList<string> Validate(string json)
    {
        JsonElement value;

        try
        {
            using var document = JsonDocument.Parse(json);
            value = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return [$"$: payload is not valid JSON ({exception.Message})"];
        }

        var errors = new List<string>();
        ValidateNode(_schema, value, "$", errors);
        return errors;
    }

    private static void ValidateNode(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        if (schema.TryGetProperty("type", out var typeElement))
        {
            var allowedTypes = typeElement.ValueKind == JsonValueKind.Array
                ? typeElement.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
                : [typeElement.GetString() ?? string.Empty];

            if (!allowedTypes.Any(type => Matches(type, value)))
            {
                errors.Add($"{path}: expected {string.Join(" or ", allowedTypes)} but found {Describe(value)}");
                return;
            }
        }

        if (schema.TryGetProperty("enum", out var enumElement)
            && enumElement.ValueKind == JsonValueKind.Array
            && !enumElement.EnumerateArray().Any(candidate => JsonEquals(candidate, value)))
        {
            var allowed = string.Join(", ", enumElement.EnumerateArray().Select(item => item.GetRawText()));
            errors.Add($"{path}: value {value.GetRawText()} is not one of [{allowed}]");
            return;
        }

        if (value.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var properties))
        {
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in required.EnumerateArray().Select(item => item.GetString() ?? string.Empty))
                {
                    if (!value.TryGetProperty(name, out _))
                    {
                        errors.Add($"{path}: required property '{name}' is missing");
                    }
                }
            }

            var allowsAdditional = !schema.TryGetProperty("additionalProperties", out var additional)
                || additional.ValueKind != JsonValueKind.False;

            foreach (var property in value.EnumerateObject())
            {
                if (properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    ValidateNode(propertySchema, property.Value, $"{path}.{property.Name}", errors);
                }
                else if (!allowsAdditional)
                {
                    errors.Add($"{path}: property '{property.Name}' is not part of the documented contract");
                }
            }
        }

        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
        {
            var index = 0;

            foreach (var item in value.EnumerateArray())
            {
                ValidateNode(items, item, $"{path}[{index}]", errors);
                index++;
            }
        }
    }

    private static bool Matches(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => IsInteger(value),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static bool IsInteger(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number
        && decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && number == decimal.Truncate(number);

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number
            && decimal.TryParse(left.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind == JsonValueKind.String
            ? string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal)
            : left.GetRawText() == right.GetRawText();
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        _ => value.ValueKind.ToString(),
    };

    private static void AssertSchemaIsSupported(JsonElement schema, string path)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new BenchmarkDataException($"Schema node {path} must be an object.");
        }

        foreach (var property in schema.EnumerateObject())
        {
            if (!SupportedKeywords.Contains(property.Name))
            {
                throw new BenchmarkDataException(
                    $"Schema node {path} uses unsupported keyword '{property.Name}'; "
                    + "extend JsonSchemaValidator before adding it to the schema.");
            }
        }

        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                AssertSchemaIsSupported(property.Value, $"{path}.{property.Name}");
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            AssertSchemaIsSupported(items, $"{path}[]");
        }
    }
}
