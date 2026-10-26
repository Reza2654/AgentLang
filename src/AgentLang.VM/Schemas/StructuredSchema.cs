using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgentLang.AST;

namespace AgentLang.VM.Schemas;

public enum SchemaType
{
    String,
    Integer,
    Float,
    Boolean,
    Array,
    Object
}

public sealed record FieldDefinition(
    string Name,
    SchemaType Type,
    bool Required = true,
    string? Description = null,
    double? MinValue = null,
    double? MaxValue = null,
    string? RegexPattern = null,
    SchemaType? ItemType = null,
    StructuredSchema? ObjectSchema = null
);

public sealed class StructuredSchema
{
    public string Name { get; }
    public string? Description { get; }
    public List<FieldDefinition> Fields { get; }

    public StructuredSchema(string name, string? description = null, IEnumerable<FieldDefinition>? fields = null)
    {
        Name = name;
        Description = description;
        Fields = fields?.ToList() ?? [];
    }

    public string ToJsonSchema()
    {
        var root = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = Name,
            ["type"] = "object"
        };

        if (!string.IsNullOrEmpty(Description))
        {
            root["description"] = Description;
        }

        var properties = new JsonObject();
        var requiredList = new JsonArray();

        foreach (var field in Fields)
        {
            var prop = new JsonObject();
            prop["type"] = field.Type switch
            {
                SchemaType.String => "string",
                SchemaType.Integer => "integer",
                SchemaType.Float => "number",
                SchemaType.Boolean => "boolean",
                SchemaType.Array => "array",
                SchemaType.Object => "object",
                _ => "string"
            };

            if (!string.IsNullOrEmpty(field.Description))
            {
                prop["description"] = field.Description;
            }

            if (field.MinValue.HasValue)
            {
                prop["minimum"] = field.MinValue.Value;
            }

            if (field.MaxValue.HasValue)
            {
                prop["maximum"] = field.MaxValue.Value;
            }

            if (!string.IsNullOrEmpty(field.RegexPattern))
            {
                prop["pattern"] = field.RegexPattern;
            }

            if (field.Type == SchemaType.Array && field.ItemType.HasValue)
            {
                prop["items"] = new JsonObject
                {
                    ["type"] = field.ItemType.Value switch
                    {
                        SchemaType.String => "string",
                        SchemaType.Integer => "integer",
                        SchemaType.Float => "number",
                        SchemaType.Boolean => "boolean",
                        _ => "string"
                    }
                };
            }

            if (field.Type == SchemaType.Object && field.ObjectSchema != null)
            {
                var subProps = new JsonObject();
                foreach (var subField in field.ObjectSchema.Fields)
                {
                    subProps[subField.Name] = new JsonObject
                    {
                        ["type"] = subField.Type.ToString().ToLowerInvariant()
                    };
                }
                prop["properties"] = subProps;
            }

            properties[field.Name] = prop;

            if (field.Required)
            {
                requiredList.Add(field.Name);
            }
        }

        root["properties"] = properties;
        if (requiredList.Count > 0)
        {
            root["required"] = requiredList;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public SchemaValidationResult Validate(string rawText) => StructuredOutputParser.Validate(rawText, this);
}

public sealed class SchemaBuilder
{
    private readonly string _name;
    private string? _description;
    private readonly List<FieldDefinition> _fields = [];

    private SchemaBuilder(string name) => _name = name;

    public static SchemaBuilder Create(string name) => new(name);

    public SchemaBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public SchemaBuilder AddString(string name, bool required = true, string? description = null, string? regexPattern = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.String, required, description, RegexPattern: regexPattern));
        return this;
    }

    public SchemaBuilder AddInteger(string name, bool required = true, string? description = null, double? min = null, double? max = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.Integer, required, description, MinValue: min, MaxValue: max));
        return this;
    }

    public SchemaBuilder AddFloat(string name, bool required = true, string? description = null, double? min = null, double? max = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.Float, required, description, MinValue: min, MaxValue: max));
        return this;
    }

    public SchemaBuilder AddBoolean(string name, bool required = true, string? description = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.Boolean, required, description));
        return this;
    }

    public SchemaBuilder AddArray(string name, SchemaType itemType = SchemaType.String, bool required = true, string? description = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.Array, required, description, ItemType: itemType));
        return this;
    }

    public SchemaBuilder AddObject(string name, StructuredSchema objectSchema, bool required = true, string? description = null)
    {
        _fields.Add(new FieldDefinition(name, SchemaType.Object, required, description, ObjectSchema: objectSchema));
        return this;
    }

    public StructuredSchema Build() => new(_name, _description, _fields);
}

public sealed class SchemaValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
    public JsonDocument? Document { get; init; }
    public Dictionary<string, object?>? Values { get; init; }
}

public static partial class StructuredOutputParser
{
    public static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var cleaned = text.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned.Substring(7).Trim();
        }
        else if (cleaned.StartsWith("```"))
        {
            cleaned = cleaned.Substring(3).Trim();
        }

        if (cleaned.EndsWith("```"))
        {
            cleaned = cleaned[..^3].Trim();
        }

        var fenceMatch = MarkdownJsonRegex().Match(cleaned);
        if (fenceMatch.Success)
        {
            cleaned = fenceMatch.Groups[1].Value.Trim();
        }

        int firstBrace = cleaned.IndexOf('{');
        int lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
        }
        else if (firstBrace >= 0)
        {
            return cleaned.Substring(firstBrace).Trim();
        }

        int firstBracket = cleaned.IndexOf('[');
        int lastBracket = cleaned.LastIndexOf(']');
        if (firstBracket >= 0 && lastBracket > firstBracket)
        {
            return cleaned.Substring(firstBracket, lastBracket - firstBracket + 1).Trim();
        }
        else if (firstBracket >= 0)
        {
            return cleaned.Substring(firstBracket).Trim();
        }

        return cleaned;
    }

    public static SchemaValidationResult Validate(string rawText, StructuredSchema schema)
    {
        var errors = new List<string>();
        string jsonText = ExtractJson(rawText);

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = ["Input did not contain valid JSON content."]
            };
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonText);
        }
        catch (JsonException ex)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = [$"JSON syntax error: {ex.Message}"]
            };
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new SchemaValidationResult
            {
                IsValid = false,
                Errors = ["Expected top-level JSON object."],
                Document = doc
            };
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in schema.Fields)
        {
            bool hasProp = false;
            JsonProperty matchedProp = default;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.Equals(field.Name, StringComparison.OrdinalIgnoreCase))
                {
                    hasProp = true;
                    matchedProp = prop;
                    break;
                }
            }

            if (!hasProp)
            {
                if (field.Required)
                {
                    errors.Add($"Required field '{field.Name}' is missing.");
                }
                continue;
            }

            var valElem = matchedProp.Value;

            // Type verification
            switch (field.Type)
            {
                case SchemaType.String:
                    if (valElem.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"Field '{field.Name}' should be a string, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        var str = valElem.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(field.RegexPattern) && !Regex.IsMatch(str, field.RegexPattern))
                        {
                            errors.Add($"Field '{field.Name}' value does not match regex pattern '{field.RegexPattern}'.");
                        }
                        values[field.Name] = str;
                    }
                    break;

                case SchemaType.Integer:
                    if (valElem.ValueKind != JsonValueKind.Number || !valElem.TryGetInt64(out var intVal))
                    {
                        errors.Add($"Field '{field.Name}' should be an integer, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        if (field.MinValue.HasValue && intVal < field.MinValue.Value)
                            errors.Add($"Field '{field.Name}' value {intVal} is below minimum {field.MinValue.Value}.");
                        if (field.MaxValue.HasValue && intVal > field.MaxValue.Value)
                            errors.Add($"Field '{field.Name}' value {intVal} exceeds maximum {field.MaxValue.Value}.");
                        values[field.Name] = intVal;
                    }
                    break;

                case SchemaType.Float:
                    if (valElem.ValueKind != JsonValueKind.Number || !valElem.TryGetDouble(out var floatVal))
                    {
                        errors.Add($"Field '{field.Name}' should be a number, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        if (field.MinValue.HasValue && floatVal < field.MinValue.Value)
                            errors.Add($"Field '{field.Name}' value {floatVal} is below minimum {field.MinValue.Value}.");
                        if (field.MaxValue.HasValue && floatVal > field.MaxValue.Value)
                            errors.Add($"Field '{field.Name}' value {floatVal} exceeds maximum {field.MaxValue.Value}.");
                        values[field.Name] = floatVal;
                    }
                    break;

                case SchemaType.Boolean:
                    if (valElem.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    {
                        errors.Add($"Field '{field.Name}' should be a boolean, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        values[field.Name] = valElem.GetBoolean();
                    }
                    break;

                case SchemaType.Array:
                    if (valElem.ValueKind != JsonValueKind.Array)
                    {
                        errors.Add($"Field '{field.Name}' should be an array, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        var list = new List<object?>();
                        foreach (var item in valElem.EnumerateArray())
                        {
                            list.Add(item.ToString());
                        }
                        values[field.Name] = list;
                    }
                    break;

                case SchemaType.Object:
                    if (valElem.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"Field '{field.Name}' should be an object, got {valElem.ValueKind}.");
                    }
                    else
                    {
                        values[field.Name] = valElem.GetRawText();
                    }
                    break;
            }
        }

        return new SchemaValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Document = doc,
            Values = values
        };
    }

    public static string AutoRepair(string malformedText)
    {
        if (string.IsNullOrWhiteSpace(malformedText)) return "{}";

        string extracted = ExtractJson(malformedText);

        // 1. Convert single quoted keys/values: 'key': 'value' -> "key": "value"
        var repaired = SingleQuoteRegex().Replace(extracted, "\"$1\"");

        // 2. Fix unquoted keys: { key: "value" } -> { "key": "value" }
        repaired = UnquotedKeyRegex().Replace(repaired, "\"$1\":");

        // 3. Remove trailing comma at end of line / EOF before closing
        repaired = Regex.Replace(repaired, @",\s*$", "");

        // 4. Ensure matched braces
        int openBraces = repaired.Count(c => c == '{');
        int closeBraces = repaired.Count(c => c == '}');
        if (openBraces > closeBraces)
        {
            repaired += new string('}', openBraces - closeBraces);
        }

        int openBrackets = repaired.Count(c => c == '[');
        int closeBrackets = repaired.Count(c => c == ']');
        if (openBrackets > closeBrackets)
        {
            repaired += new string(']', openBrackets - closeBrackets);
        }

        // 5. Remove trailing commas before closing braces/brackets: [1, 2,] -> [1, 2]
        repaired = TrailingCommaRegex().Replace(repaired, "$1");

        return repaired;
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownJsonRegex();

    [GeneratedRegex(@"'([^'\\]*(?:\\.[^'\\]*)*)'")]
    private static partial Regex SingleQuoteRegex();

    [GeneratedRegex(@"(?<=[{,]\s*)([a-zA-Z_][a-zA-Z0-9_]*)\s*:")]
    private static partial Regex UnquotedKeyRegex();

    [GeneratedRegex(@",\s*([\]\}])")]
    private static partial Regex TrailingCommaRegex();
}

public static class SchemaAstExtensions
{
    public static StructuredSchema ToStructuredSchema(this SchemaDeclarationNode node)
    {
        var builder = SchemaBuilder.Create(node.Name);
        if (!string.IsNullOrEmpty(node.Description))
        {
            builder.WithDescription(node.Description);
        }

        foreach (var f in node.Fields)
        {
            if (f.IsArray)
            {
                var itemType = ParseSchemaType(f.TypeName);
                builder.AddArray(f.Name, itemType, f.IsRequired, f.Description);
            }
            else
            {
                var type = ParseSchemaType(f.TypeName);
                switch (type)
                {
                    case SchemaType.Integer:
                        builder.AddInteger(f.Name, f.IsRequired, f.Description, f.MinValue, f.MaxValue);
                        break;
                    case SchemaType.Float:
                        builder.AddFloat(f.Name, f.IsRequired, f.Description, f.MinValue, f.MaxValue);
                        break;
                    case SchemaType.Boolean:
                        builder.AddBoolean(f.Name, f.IsRequired, f.Description);
                        break;
                    default:
                        builder.AddString(f.Name, f.IsRequired, f.Description, f.RegexPattern);
                        break;
                }
            }
        }

        return builder.Build();
    }

    private static SchemaType ParseSchemaType(string typeName) => typeName.ToLowerInvariant() switch
    {
        "int" or "integer" or "long" => SchemaType.Integer,
        "float" or "double" or "number" => SchemaType.Float,
        "bool" or "boolean" => SchemaType.Boolean,
        "array" or "list" => SchemaType.Array,
        "object" => SchemaType.Object,
        _ => SchemaType.String
    };
}
