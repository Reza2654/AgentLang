using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLang.Errors;
using AgentLang.Runtime.Data;
using AgentLang.Runtime.Values;

namespace AgentLang.Runtime.Stdlib;

public static class StandardLibrary
{
    public static bool IsStdlibModule(string modulePath) =>
        modulePath.StartsWith("@stdlib/", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, BuiltinFunctionValue> GetModuleFunctions(string modulePath)
    {
        var mod = modulePath.Trim().ToLowerInvariant();
        return mod switch
        {
            "@stdlib/crypto" => CryptoModule,
            "@stdlib/text" => TextModule,
            "@stdlib/rag" => RagModule,
            "@stdlib/json" => JsonModule,
            "@stdlib/math" => MathModule,
            "@stdlib/time" => TimeModule,
            "@stdlib/collections" => CollectionsModule,
            "@stdlib/env" => EnvModule,
            _ => throw new AgentLangRuntimeException($"Unknown standard library module: '{modulePath}'", errorCode: "STD001")
        };
    }

    public static object? ConvertJsonElement(JsonElement elem) => elem.ValueKind switch
    {
        JsonValueKind.Object => elem.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.OrdinalIgnoreCase),
        JsonValueKind.Array => elem.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.String => elem.GetString(),
        JsonValueKind.Number => elem.TryGetInt64(out long l) ? l : elem.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => elem.ToString()
    };

    public static bool TryInvoke(string moduleOrFunc, string? member, IReadOnlyList<object?> args, out object? result)
    {
        result = null;
        if (!string.IsNullOrEmpty(member))
        {
            IReadOnlyDictionary<string, BuiltinFunctionValue>? mod = moduleOrFunc.ToLowerInvariant() switch
            {
                "text" or "@stdlib/text" => TextModule,
                "json" or "@stdlib/json" => JsonModule,
                "math" or "@stdlib/math" => MathModule,
                "time" or "@stdlib/time" => TimeModule,
                "collections" or "@stdlib/collections" => CollectionsModule,
                "crypto" or "@stdlib/crypto" => CryptoModule,
                "rag" or "@stdlib/rag" => RagModule,
                "env" or "@stdlib/env" => EnvModule,
                _ => null
            };

            if (mod != null && mod.TryGetValue(member, out var fn))
            {
                result = fn.Execute(args);
                return true;
            }
            return false;
        }

        // Global function invocation
        string funcName = moduleOrFunc.ToLowerInvariant();
        if (funcName is "len" or "length") { result = CollectionsModule["len"].Execute(args); return true; }
        if (funcName is "trim") { result = TextModule["trim"].Execute(args); return true; }
        if (funcName is "split") { result = TextModule["split"].Execute(args); return true; }
        if (funcName is "join") { result = TextModule["join"].Execute(args); return true; }
        if (funcName is "replace") { result = TextModule["replace"].Execute(args); return true; }
        if (funcName is "index_of") { result = TextModule["index_of"].Execute(args); return true; }
        if (funcName is "to_lower") { result = TextModule["to_lower"].Execute(args); return true; }
        if (funcName is "to_upper") { result = TextModule["to_upper"].Execute(args); return true; }
        if (funcName is "starts_with") { result = TextModule["starts_with"].Execute(args); return true; }
        if (funcName is "ends_with") { result = TextModule["ends_with"].Execute(args); return true; }
        if (funcName is "contains") { result = CollectionsModule["contains"].Execute(args); return true; }
        if (funcName is "unique") { result = CollectionsModule["unique"].Execute(args); return true; }
        if (funcName is "slice") { result = CollectionsModule["slice"].Execute(args); return true; }
        if (funcName is "env_get") { result = EnvModule["get"].Execute(args); return true; }
        if (funcName is "env_has") { result = EnvModule["has"].Execute(args); return true; }
        if (funcName is "round") { result = MathModule["round"].Execute(args); return true; }
        if (funcName is "min") { result = MathModule["min"].Execute(args); return true; }
        if (funcName is "max") { result = MathModule["max"].Execute(args); return true; }
        if (funcName is "abs") { result = MathModule["abs"].Execute(args); return true; }
        if (funcName is "clamp") { result = MathModule["clamp"].Execute(args); return true; }
        if (funcName is "json_parse") { result = JsonModule["parse"].Execute(args); return true; }
        if (funcName is "json_stringify") { result = JsonModule["stringify"].Execute(args); return true; }
        if (funcName is "time_now") { result = TimeModule["now"].Execute(args); return true; }
        if (funcName is "time_iso") { result = TimeModule["iso"].Execute(args); return true; }
        if (funcName is "time_format") { result = TimeModule["format"].Execute(args); return true; }
        if (funcName is "time_elapsed") { result = TimeModule["elapsed_ms"].Execute(args); return true; }
        if (funcName is "sleep_ms") { result = TimeModule["sleep_ms"].Execute(args); return true; }
        if (funcName is "pow" or "math_pow") { result = MathModule["pow"].Execute(args); return true; }
        if (funcName is "sqrt" or "math_sqrt") { result = MathModule["sqrt"].Execute(args); return true; }

        return false;
    }

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> CryptoModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["sha256"] = new BuiltinFunctionValue("sha256", args =>
            {
                string input = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = SHA256.HashData(bytes);
                return Convert.ToHexStringLower(hash);
            }),

            ["md5"] = new BuiltinFunctionValue("md5", args =>
            {
                string input = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                byte[] hash = MD5.HashData(bytes);
                return Convert.ToHexStringLower(hash);
            }),

            ["uuid"] = new BuiltinFunctionValue("uuid", _ => Guid.NewGuid().ToString()),

            ["random_token"] = new BuiltinFunctionValue("random_token", args =>
            {
                int len = 32;
                if (args.Count > 0 && int.TryParse(args[0]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLen) && parsedLen > 0)
                {
                    len = Math.Clamp(parsedLen, 1, 65536);
                }
                return RandomNumberGenerator.GetHexString(len, lowercase: true);
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> TextModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["split"] = new BuiltinFunctionValue("split", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string delim = args.Count > 1 ? (args[1]?.ToString() ?? " ") : " ";
                var parts = text.Split(delim, StringSplitOptions.None);
                return parts.Cast<object?>().ToList();
            }),

            ["join"] = new BuiltinFunctionValue("join", args =>
            {
                if (args.Count == 0 || args[0] == null) return string.Empty;
                string delim = args.Count > 1 ? (args[1]?.ToString() ?? "") : "";
                if (args[0] is System.Collections.IEnumerable enumerable && args[0] is not string)
                {
                    var items = new List<string>();
                    foreach (var item in enumerable)
                    {
                        items.Add(item?.ToString() ?? string.Empty);
                    }
                    return string.Join(delim, items);
                }
                return args[0]?.ToString() ?? string.Empty;
            }),

            ["trim"] = new BuiltinFunctionValue("trim", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                return text.Trim();
            }),

            ["matches"] = new BuiltinFunctionValue("matches", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string pattern = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(pattern)) return false;
                try
                {
                    return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException ex)
                {
                    throw new AgentLangRuntimeException($"Invalid regex pattern '{pattern}': {ex.Message}", errorCode: "STD-REGEX-ERR", innerException: ex);
                }
                catch (RegexMatchTimeoutException ex)
                {
                    throw new AgentLangRuntimeException($"Regex match timed out for pattern '{pattern}'", errorCode: "STD-REGEX-TIMEOUT", innerException: ex);
                }
            }),

            ["replace"] = new BuiltinFunctionValue("replace", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string oldVal = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;
                string newVal = args.Count > 2 ? (args[2]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(oldVal)) return text;
                return text.Replace(oldVal, newVal, StringComparison.Ordinal);
            }),

            ["to_lower"] = new BuiltinFunctionValue("to_lower", args =>
                args.Count > 0 ? (args[0]?.ToString() ?? string.Empty).ToLowerInvariant() : string.Empty),

            ["to_upper"] = new BuiltinFunctionValue("to_upper", args =>
                args.Count > 0 ? (args[0]?.ToString() ?? string.Empty).ToUpperInvariant() : string.Empty),

            ["starts_with"] = new BuiltinFunctionValue("starts_with", args =>
                args.Count > 1 && (args[0]?.ToString() ?? string.Empty).StartsWith(args[1]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)),

            ["ends_with"] = new BuiltinFunctionValue("ends_with", args =>
                args.Count > 1 && (args[0]?.ToString() ?? string.Empty).EndsWith(args[1]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)),

            ["contains"] = new BuiltinFunctionValue("contains", args =>
                args.Count > 1 && (args[0]?.ToString() ?? string.Empty).Contains(args[1]?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase)),

            ["index_of"] = new BuiltinFunctionValue("index_of", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string sub = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;
                return text.IndexOf(sub, StringComparison.Ordinal);
            }),

            ["len"] = new BuiltinFunctionValue("len", args =>
                args.Count > 0 ? (args[0]?.ToString() ?? string.Empty).Length : 0)
        };

    private static double ToDoubleSafe(object? value, double fallback = 0.0)
    {
        if (value == null) return fallback;
        return value switch
        {
            double d => double.IsNaN(d) || double.IsInfinity(d) ? fallback : d,
            float f => float.IsNaN(f) || float.IsInfinity(f) ? fallback : f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            bool b => b ? 1.0 : 0.0,
            _ => double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback
        };
    }

    private static int ToIntSafe(object? value, int fallback = 0)
    {
        if (value == null) return fallback;
        return value switch
        {
            int i => i,
            long l => (int)Math.Clamp(l, int.MinValue, int.MaxValue),
            double d => double.IsNaN(d) || double.IsInfinity(d) ? fallback : (int)Math.Clamp(d, int.MinValue, int.MaxValue),
            float f => float.IsNaN(f) || float.IsInfinity(f) ? fallback : (int)Math.Clamp(f, int.MinValue, int.MaxValue),
            decimal m => (int)Math.Clamp(m, int.MinValue, int.MaxValue),
            bool b => b ? 1 : 0,
            _ => int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : (int)ToDoubleSafe(value, fallback)
        };
    }

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> JsonModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["parse"] = new BuiltinFunctionValue("parse", args =>
            {
                string json = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(json)) return null;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    return ConvertJsonElement(doc.RootElement);
                }
                catch (JsonException ex)
                {
                    throw new AgentLangRuntimeException($"Invalid JSON payload: {ex.Message}", errorCode: "STD-JSON-ERR", innerException: ex);
                }
            }),

            ["stringify"] = new BuiltinFunctionValue("stringify", args =>
            {
                if (args.Count == 0 || args[0] == null) return "null";
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    SchemaEntityStore.WriteJsonValue(writer, args[0]);
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }),

            ["is_valid"] = new BuiltinFunctionValue("is_valid", args =>
            {
                string json = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(json)) return false;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    return true;
                }
                catch
                {
                    return false;
                }
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> MathModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["min"] = new BuiltinFunctionValue("min", args =>
            {
                double a = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                double b = args.Count > 1 ? ToDoubleSafe(args[1]) : 0;
                return Math.Min(a, b);
            }),

            ["max"] = new BuiltinFunctionValue("max", args =>
            {
                double a = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                double b = args.Count > 1 ? ToDoubleSafe(args[1]) : 0;
                return Math.Max(a, b);
            }),

            ["round"] = new BuiltinFunctionValue("round", args =>
            {
                double val = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                int decimals = args.Count > 1 ? Math.Clamp(ToIntSafe(args[1]), 0, 15) : 0;
                return Math.Round(val, decimals, MidpointRounding.AwayFromZero);
            }),

            ["abs"] = new BuiltinFunctionValue("abs", args =>
                args.Count > 0 ? Math.Abs(ToDoubleSafe(args[0])) : 0),

            ["clamp"] = new BuiltinFunctionValue("clamp", args =>
            {
                double val = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                double min = args.Count > 1 ? ToDoubleSafe(args[1]) : 0;
                double max = args.Count > 2 ? ToDoubleSafe(args[2]) : 0;
                if (min > max) (min, max) = (max, min);
                return Math.Clamp(val, min, max);
            }),

            ["floor"] = new BuiltinFunctionValue("floor", args =>
                args.Count > 0 ? Math.Floor(ToDoubleSafe(args[0])) : 0),

            ["ceil"] = new BuiltinFunctionValue("ceil", args =>
                args.Count > 0 ? Math.Ceiling(ToDoubleSafe(args[0])) : 0),

            ["pow"] = new BuiltinFunctionValue("pow", args =>
            {
                double b = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                double exp = args.Count > 1 ? ToDoubleSafe(args[1], 1.0) : 1;
                double res = Math.Pow(b, exp);
                return double.IsNaN(res) || double.IsInfinity(res) ? 0.0 : res;
            }),

            ["sqrt"] = new BuiltinFunctionValue("sqrt", args =>
            {
                double val = args.Count > 0 ? ToDoubleSafe(args[0]) : 0;
                return Math.Sqrt(Math.Max(0, val));
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> TimeModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["now"] = new BuiltinFunctionValue("now", _ => DateTime.UtcNow.ToString("o")),
            ["iso"] = new BuiltinFunctionValue("iso", _ => DateTime.UtcNow.ToString("o")),
            ["timestamp"] = new BuiltinFunctionValue("timestamp", _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ["format"] = new BuiltinFunctionValue("format", args =>
            {
                if (args.Count == 0 || args[0] == null) return string.Empty;
                string fmt = args.Count > 1 && !string.IsNullOrWhiteSpace(args[1]?.ToString())
                    ? args[1]!.ToString()!
                    : "yyyy-MM-dd HH:mm:ss";

                DateTime dt = DateTime.UtcNow;
                if (args[0] is long l)
                {
                    dt = DateTimeOffset.FromUnixTimeMilliseconds(l).UtcDateTime;
                }
                else if (args[0] is int i)
                {
                    dt = DateTimeOffset.FromUnixTimeMilliseconds(i).UtcDateTime;
                }
                else if (args[0] is double d && !double.IsNaN(d) && !double.IsInfinity(d))
                {
                    dt = DateTimeOffset.FromUnixTimeMilliseconds((long)d).UtcDateTime;
                }
                else
                {
                    string s = args[0]?.ToString() ?? "";
                    if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsedDt) ||
                        DateTime.TryParse(s, out parsedDt))
                    {
                        dt = parsedDt.ToUniversalTime();
                    }
                    else if (long.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ms))
                    {
                        dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
                    }
                }

                try
                {
                    return dt.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                }
            }),
            ["sleep_ms"] = new BuiltinFunctionValue("sleep_ms", args =>
            {
                int ms = args.Count > 0 && int.TryParse(args[0]?.ToString(), out int val)
                    ? Math.Clamp(val, 0, 300_000)
                    : 0;
                if (ms > 0)
                {
                    Thread.Sleep(ms);
                }
                return ms;
            }),
            ["elapsed_ms"] = new BuiltinFunctionValue("elapsed_ms", args =>
            {
                if (args.Count == 0 || args[0] == null) return 0.0;
                if (args[0] is long msVal)
                    return (double)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - msVal);
                if (args[0] is double dVal && !double.IsNaN(dVal) && !double.IsInfinity(dVal))
                    return (double)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)dVal);
                string s = args[0]?.ToString() ?? "";
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt) ||
                    DateTime.TryParse(s, out dt))
                    return (DateTime.UtcNow - dt.ToUniversalTime()).TotalMilliseconds;
                if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                    return (double)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - ms);
                return 0.0;
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> CollectionsModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["len"] = new BuiltinFunctionValue("len", args =>
            {
                if (args.Count == 0 || args[0] == null) return 0;
                if (args[0] is string s) return s.Length;
                if (args[0] is System.Collections.ICollection c) return c.Count;
                if (args[0] is System.Collections.IEnumerable en) return en.Cast<object?>().Count();
                return 0;
            }),

            ["contains"] = new BuiltinFunctionValue("contains", args =>
            {
                if (args.Count < 2 || args[0] == null) return false;
                var itemStr = args[1]?.ToString();
                if (args[0] is string s) return s.Contains(itemStr ?? "", StringComparison.OrdinalIgnoreCase);
                if (args[0] is System.Collections.IEnumerable en)
                    return en.Cast<object?>().Any(o => Equals(o?.ToString(), itemStr));
                return false;
            }),

            ["join"] = new BuiltinFunctionValue("join", args =>
            {
                if (args.Count == 0 || args[0] == null) return string.Empty;
                string delim = args.Count > 1 ? (args[1]?.ToString() ?? "") : "";
                if (args[0] is System.Collections.IEnumerable en && args[0] is not string)
                    return string.Join(delim, en.Cast<object?>().Select(o => o?.ToString() ?? ""));
                return args[0]?.ToString() ?? string.Empty;
            }),

            ["append"] = new BuiltinFunctionValue("append", args =>
            {
                if (args.Count == 0) return new List<object?>();
                var list = new List<object?>();
                if (args[0] is System.Collections.IEnumerable en && args[0] is not string)
                    list.AddRange(en.Cast<object?>());
                else if (args[0] != null)
                    list.Add(args[0]);
                if (args.Count > 1)
                    list.Add(args[1]);
                return list;
            }),

            ["reverse"] = new BuiltinFunctionValue("reverse", args =>
            {
                if (args.Count == 0 || args[0] == null) return new List<object?>();
                if (args[0] is System.Collections.IEnumerable en && args[0] is not string)
                {
                    var list = en.Cast<object?>().ToList();
                    list.Reverse();
                    return list;
                }
                return new List<object?> { args[0] };
            }),

            ["unique"] = new BuiltinFunctionValue("unique", args =>
            {
                if (args.Count == 0 || args[0] == null) return new List<object?>();
                if (args[0] is System.Collections.IEnumerable en && args[0] is not string)
                {
                    var seen = new HashSet<string>();
                    var result = new List<object?>();
                    foreach (var item in en.Cast<object?>())
                    {
                        string key = item?.ToString() ?? "\0null";
                        if (seen.Add(key))
                        {
                            result.Add(item);
                        }
                    }
                    return result;
                }
                return new List<object?> { args[0] };
            }),

            ["slice"] = new BuiltinFunctionValue("slice", args =>
            {
                if (args.Count == 0 || args[0] == null) return new List<object?>();
                if (args[0] is System.Collections.IEnumerable en && args[0] is not string)
                {
                    var list = en.Cast<object?>().ToList();
                    int start = args.Count > 1 ? Math.Max(0, ToIntSafe(args[1])) : 0;
                    int count = args.Count > 2 ? Math.Max(0, ToIntSafe(args[2])) : list.Count - start;
                    if (start >= list.Count) return new List<object?>();
                    int take = Math.Min(count, list.Count - start);
                    return list.GetRange(start, take);
                }
                return new List<object?>();
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> RagModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["chunk_text"] = new BuiltinFunctionValue("chunk_text", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                int chunkSize = 200;
                int overlap = 20;

                if (args.Count > 1 && int.TryParse(args[1]?.ToString(), out int cs) && cs > 0)
                    chunkSize = cs;
                if (args.Count > 2 && int.TryParse(args[2]?.ToString(), out int ov) && ov >= 0)
                    overlap = Math.Min(ov, chunkSize - 1);

                var chunks = new List<object?>();
                if (string.IsNullOrEmpty(text))
                {
                    return chunks;
                }

                int step = Math.Max(1, chunkSize - overlap);
                for (int i = 0; i < text.Length; i += step)
                {
                    int len = Math.Min(chunkSize, text.Length - i);
                    chunks.Add(text.Substring(i, len));
                    if (i + len >= text.Length) break;
                }

                return chunks;
            }),

            ["similarity"] = new BuiltinFunctionValue("similarity", args =>
            {
                string textA = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string textB = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;

                if (string.Equals(textA, textB, StringComparison.OrdinalIgnoreCase))
                    return 1.0;
                if (string.IsNullOrEmpty(textA) || string.IsNullOrEmpty(textB))
                    return 0.0;

                // Word-based Jaccard + Trigram hybrid similarity
                var wordsA = textA.Split([' ', '\t', '\r', '\n', ',', '.', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
                                  .Select(w => w.ToLowerInvariant()).ToHashSet();
                var wordsB = textB.Split([' ', '\t', '\r', '\n', ',', '.', ';', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
                                  .Select(w => w.ToLowerInvariant()).ToHashSet();

                if (wordsA.Count == 0 || wordsB.Count == 0) return 0.0;

                int intersection = wordsA.Count(w => wordsB.Contains(w));
                int union = wordsA.Union(wordsB).Count();

                double score = union > 0 ? (double)intersection / union : 0.0;
                return Math.Round(score, 4);
            })
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> EnvModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["get"] = new BuiltinFunctionValue("get", args =>
            {
                string name = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string def = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(name)) return def;
                return Environment.GetEnvironmentVariable(name) ?? def;
            }),

            ["has"] = new BuiltinFunctionValue("has", args =>
            {
                string name = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrEmpty(name)) return false;
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
            }),

            ["all"] = new BuiltinFunctionValue("all", _ =>
            {
                var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                {
                    dict[entry.Key.ToString() ?? ""] = entry.Value?.ToString() ?? "";
                }
                return dict;
            })
        };
}
