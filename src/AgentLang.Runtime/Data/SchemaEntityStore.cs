using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Runtime.Stdlib;

namespace AgentLang.Runtime.Data;

public sealed record EntityValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Schema-enforced persistent entity store backing both `.agt` data operations
/// and auto-generated REST endpoints with zero external dependencies.
/// </summary>
public sealed class SchemaEntityStore
{
    private readonly Dictionary<string, SchemaDeclarationNode> _schemas = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, object?>>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public string? StorageDirectory { get; set; }

    public SchemaEntityStore(string? storageDirectory = null)
    {
        StorageDirectory = storageDirectory;
    }

    public void RegisterSchema(SchemaDeclarationNode schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        lock (_sync)
        {
            _schemas[schema.Name] = schema;
            if (!_tables.ContainsKey(schema.Name))
            {
                _tables[schema.Name] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
                LoadFromDiskIfConfigured(schema.Name);
            }
        }
    }

    public bool TryGetSchema(string schemaName, out SchemaDeclarationNode schema)
    {
        lock (_sync)
        {
            return _schemas.TryGetValue(schemaName, out schema!);
        }
    }

    public IReadOnlyList<SchemaDeclarationNode> GetSchemas()
    {
        lock (_sync)
        {
            return _schemas.Values.ToList();
        }
    }

    public EntityValidationResult Validate(string schemaName, IDictionary<string, object?> record)
    {
        SchemaDeclarationNode schema;
        lock (_sync)
        {
            if (!_schemas.TryGetValue(schemaName, out schema!))
            {
                return new EntityValidationResult(false, [$"Unknown schema '{schemaName}'."]);
            }
        }

        var errors = new List<string>();
        var ciRecord = new Dictionary<string, object?>(record, StringComparer.OrdinalIgnoreCase);

        foreach (var field in schema.Fields)
        {
            bool hasValue = ciRecord.TryGetValue(field.Name, out var val) && val != null;
            if (!hasValue)
            {
                if (field.IsRequired)
                {
                    errors.Add($"Missing required field '{field.Name}' on schema '{schema.Name}'.");
                }
                continue;
            }

            if (field.IsArray)
            {
                if (val is not System.Collections.IEnumerable enumerable || val is string)
                {
                    errors.Add($"Field '{field.Name}' must be an array of {field.TypeName}.");
                    continue;
                }

                int idx = 0;
                foreach (var item in enumerable)
                {
                    ValidateScalarField($"{field.Name}[{idx}]", field, item, errors);
                    idx++;
                }
            }
            else
            {
                ValidateScalarField(field.Name, field, val, errors);
            }
        }

        return new EntityValidationResult(errors.Count == 0, errors);
    }

    public Dictionary<string, object?> Insert(string schemaName, IDictionary<string, object?> input, string? customId = null)
    {
        lock (_sync)
        {
            EnsureSchemaRegistered(schemaName);
            var record = CloneRecord(input);

            string id = !string.IsNullOrWhiteSpace(customId)
                ? customId
                : (record.TryGetValue("id", out var existingId) && !string.IsNullOrWhiteSpace(existingId?.ToString())
                    ? existingId.ToString()!
                    : Guid.NewGuid().ToString("N")[..12]);

            record["id"] = id;
            var validation = Validate(schemaName, record);
            if (!validation.IsValid)
            {
                throw new AgentLangRuntimeException(
                    $"Schema validation failed for '{schemaName}': {string.Join("; ", validation.Errors)}",
                    errorCode: "AL-DATA-001");
            }

            string now = DateTimeOffset.UtcNow.ToString("O");
            record["_created_at"] = now;
            record["_updated_at"] = now;

            _tables[schemaName][id] = record;
            SaveToDiskIfConfigured(schemaName);
            return CloneRecord(record);
        }
    }

    public Dictionary<string, object?>? Get(string schemaName, string id)
    {
        lock (_sync)
        {
            EnsureSchemaRegistered(schemaName);
            return _tables[schemaName].TryGetValue(id, out var record) ? CloneRecord(record) : null;
        }
    }

    public List<Dictionary<string, object?>> List(string schemaName, IDictionary<string, object?>? filter = null)
    {
        lock (_sync)
        {
            EnsureSchemaRegistered(schemaName);
            var results = new List<Dictionary<string, object?>>();

            foreach (var record in _tables[schemaName].Values)
            {
                if (filter == null || filter.Count == 0 || MatchesFilter(record, filter))
                {
                    results.Add(CloneRecord(record));
                }
            }

            return results;
        }
    }

    public Dictionary<string, object?>? Update(string schemaName, string id, IDictionary<string, object?> patch)
    {
        lock (_sync)
        {
            EnsureSchemaRegistered(schemaName);
            if (!_tables[schemaName].TryGetValue(id, out var existing))
            {
                return null;
            }

            var merged = CloneRecord(existing);
            foreach (var (k, v) in patch)
            {
                if (k.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    k.Equals("_created_at", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                merged[k] = v;
            }

            var validation = Validate(schemaName, merged);
            if (!validation.IsValid)
            {
                throw new AgentLangRuntimeException(
                    $"Schema validation failed for '{schemaName}': {string.Join("; ", validation.Errors)}",
                    errorCode: "AL-DATA-001");
            }

            merged["_updated_at"] = DateTimeOffset.UtcNow.ToString("O");
            _tables[schemaName][id] = merged;
            SaveToDiskIfConfigured(schemaName);
            return CloneRecord(merged);
        }
    }

    public bool Delete(string schemaName, string id)
    {
        lock (_sync)
        {
            EnsureSchemaRegistered(schemaName);
            bool removed = _tables[schemaName].Remove(id);
            if (removed)
            {
                SaveToDiskIfConfigured(schemaName);
            }
            return removed;
        }
    }

    public int Count(string schemaName)
    {
        lock (_sync)
        {
            return _tables.TryGetValue(schemaName, out var table) ? table.Count : 0;
        }
    }

    private void EnsureSchemaRegistered(string schemaName)
    {
        if (!_schemas.ContainsKey(schemaName))
        {
            throw new AgentLangRuntimeException(
                $"Schema '{schemaName}' is not registered. Declare `schema {schemaName} {{ ... }}` before accessing entity data.",
                errorCode: "AL-DATA-002");
        }

        if (!_tables.ContainsKey(schemaName))
        {
            _tables[schemaName] = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool MatchesFilter(Dictionary<string, object?> record, IDictionary<string, object?> filter)
    {
        foreach (var (key, expected) in filter)
        {
            if (!record.TryGetValue(key, out var actual))
                return false;

            if (expected == null && actual == null) continue;
            if (expected == null || actual == null) return false;

            if (!string.Equals(actual.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static void ValidateScalarField(string fieldLabel, SchemaFieldNode field, object? value, List<string> errors)
    {
        if (value == null)
        {
            errors.Add($"Field '{fieldLabel}' cannot contain null.");
            return;
        }

        string t = field.TypeName.ToLowerInvariant();
        if (t is "int" or "integer" or "long")
        {
            if (!TryConvertToDouble(value, out double num) || Math.Abs(num - Math.Truncate(num)) > 1e-9)
            {
                errors.Add($"Field '{fieldLabel}' must be an integer.");
                return;
            }
            CheckNumericBounds(fieldLabel, field, num, errors);
        }
        else if (t is "float" or "double" or "number")
        {
            if (!TryConvertToDouble(value, out double num))
            {
                errors.Add($"Field '{fieldLabel}' must be a number.");
                return;
            }
            CheckNumericBounds(fieldLabel, field, num, errors);
        }
        else if (t is "bool" or "boolean")
        {
            if (value is not bool && !bool.TryParse(value.ToString(), out _))
            {
                errors.Add($"Field '{fieldLabel}' must be a boolean.");
            }
        }
        else if (t is "string" or "text")
        {
            string strVal = value.ToString() ?? "";
            if (!string.IsNullOrEmpty(field.RegexPattern))
            {
                try
                {
                    if (!Regex.IsMatch(strVal, field.RegexPattern))
                    {
                        errors.Add($"Field '{fieldLabel}' value '{strVal}' does not match pattern '{field.RegexPattern}'.");
                    }
                }
                catch (ArgumentException)
                {
                    errors.Add($"Field '{fieldLabel}' has invalid regex constraint '{field.RegexPattern}'.");
                }
            }
        }
    }

    private static void CheckNumericBounds(string fieldLabel, SchemaFieldNode field, double num, List<string> errors)
    {
        if (field.MinValue.HasValue && num < field.MinValue.Value)
            errors.Add($"Field '{fieldLabel}' value {num} is less than minimum {field.MinValue.Value}.");
        if (field.MaxValue.HasValue && num > field.MaxValue.Value)
            errors.Add($"Field '{fieldLabel}' value {num} exceeds maximum {field.MaxValue.Value}.");
    }

    private static bool TryConvertToDouble(object value, out double number)
    {
        switch (value)
        {
            case int i: number = i; return true;
            case long l: number = l; return true;
            case double d: number = d; return true;
            case float f: number = f; return true;
            case decimal m: number = (double)m; return true;
            default:
                return double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
        }
    }

    private static Dictionary<string, object?> CloneRecord(IDictionary<string, object?> source)
    {
        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in source)
        {
            if (v is IEnumerable<object?> list && v is not string)
            {
                copy[k] = list.ToList();
            }
            else if (v is Dictionary<string, object?> nested)
            {
                copy[k] = CloneRecord(nested);
            }
            else
            {
                copy[k] = v;
            }
        }
        return copy;
    }

    private void LoadFromDiskIfConfigured(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(StorageDirectory)) return;

        string path = GetTableFilePath(schemaName);
        if (!File.Exists(path)) return;

        try
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                if (StandardLibrary.ConvertJsonElement(elem) is Dictionary<string, object?> rec &&
                    rec.TryGetValue("id", out var idObj) &&
                    !string.IsNullOrWhiteSpace(idObj?.ToString()))
                {
                    _tables[schemaName][idObj.ToString()!] = rec;
                }
            }
        }
        catch
        {
            // Ignore malformed disk cache gracefully
        }
    }

    private void SaveToDiskIfConfigured(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(StorageDirectory)) return;

        Directory.CreateDirectory(StorageDirectory);
        string path = GetTableFilePath(schemaName);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var record in _tables[schemaName].Values)
            {
                WriteJsonValue(writer, record);
            }
            writer.WriteEndArray();
        }
        File.WriteAllBytes(path, ms.ToArray());
    }

    private string GetTableFilePath(string schemaName)
    {
        string safeName = string.Concat(schemaName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'));
        return Path.Combine(StorageDirectory!, $"{safeName}.json");
    }

    public static void WriteJsonValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            case float f:
                writer.WriteNumberValue(f);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case IDictionary<string, object?> dict:
                writer.WriteStartObject();
                foreach (var (k, v) in dict)
                {
                    writer.WritePropertyName(k);
                    WriteJsonValue(writer, v);
                }
                writer.WriteEndObject();
                break;
            case System.Collections.IEnumerable seq:
                writer.WriteStartArray();
                foreach (var item in seq)
                {
                    WriteJsonValue(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(value.ToString() ?? "");
                break;
        }
    }
}
