using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLang.Errors;
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
        if (funcName is "to_lower") { result = TextModule["to_lower"].Execute(args); return true; }
        if (funcName is "to_upper") { result = TextModule["to_upper"].Execute(args); return true; }
        if (funcName is "starts_with") { result = TextModule["starts_with"].Execute(args); return true; }
        if (funcName is "ends_with") { result = TextModule["ends_with"].Execute(args); return true; }
        if (funcName is "contains") { result = CollectionsModule["contains"].Execute(args); return true; }
        if (funcName is "round") { result = MathModule["round"].Execute(args); return true; }
        if (funcName is "min") { result = MathModule["min"].Execute(args); return true; }
        if (funcName is "max") { result = MathModule["max"].Execute(args); return true; }
        if (funcName is "abs") { result = MathModule["abs"].Execute(args); return true; }
        if (funcName is "clamp") { result = MathModule["clamp"].Execute(args); return true; }
        if (funcName is "json_parse") { result = JsonModule["parse"].Execute(args); return true; }
        if (funcName is "json_stringify") { result = JsonModule["stringify"].Execute(args); return true; }
        if (funcName is "time_now") { result = TimeModule["now"].Execute(args); return true; }
        if (funcName is "time_elapsed") { result = TimeModule["elapsed_ms"].Execute(args); return true; }

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
                if (args.Count > 0 && int.TryParse(args[0]?.ToString(), out int parsedLen) && parsedLen > 0)
                {
                    len = parsedLen;
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
                return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
            }),

            ["replace"] = new BuiltinFunctionValue("replace", args =>
            {
                string text = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                string oldVal = args.Count > 1 ? (args[1]?.ToString() ?? string.Empty) : string.Empty;
                string newVal = args.Count > 2 ? (args[2]?.ToString() ?? string.Empty) : string.Empty;
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

            ["len"] = new BuiltinFunctionValue("len", args =>
                args.Count > 0 ? (args[0]?.ToString() ?? string.Empty).Length : 0)
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> JsonModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["parse"] = new BuiltinFunctionValue("parse", args =>
            {
                string json = args.Count > 0 ? (args[0]?.ToString() ?? string.Empty) : string.Empty;
                if (string.IsNullOrWhiteSpace(json)) return null;
                using var doc = JsonDocument.Parse(json);
                return ConvertJsonElement(doc.RootElement);
            }),

            ["stringify"] = new BuiltinFunctionValue("stringify", args =>
            {
                if (args.Count == 0 || args[0] == null) return "null";
                return JsonSerializer.Serialize(args[0], new JsonSerializerOptions { WriteIndented = false });
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
                double a = args.Count > 0 ? Convert.ToDouble(args[0]) : 0;
                double b = args.Count > 1 ? Convert.ToDouble(args[1]) : 0;
                return Math.Min(a, b);
            }),

            ["max"] = new BuiltinFunctionValue("max", args =>
            {
                double a = args.Count > 0 ? Convert.ToDouble(args[0]) : 0;
                double b = args.Count > 1 ? Convert.ToDouble(args[1]) : 0;
                return Math.Max(a, b);
            }),

            ["round"] = new BuiltinFunctionValue("round", args =>
            {
                double val = args.Count > 0 ? Convert.ToDouble(args[0]) : 0;
                int decimals = args.Count > 1 ? Convert.ToInt32(args[1]) : 0;
                return Math.Round(val, decimals);
            }),

            ["abs"] = new BuiltinFunctionValue("abs", args =>
                args.Count > 0 ? Math.Abs(Convert.ToDouble(args[0])) : 0),

            ["clamp"] = new BuiltinFunctionValue("clamp", args =>
            {
                double val = args.Count > 0 ? Convert.ToDouble(args[0]) : 0;
                double min = args.Count > 1 ? Convert.ToDouble(args[1]) : 0;
                double max = args.Count > 2 ? Convert.ToDouble(args[2]) : 0;
                return Math.Clamp(val, min, max);
            }),

            ["floor"] = new BuiltinFunctionValue("floor", args =>
                args.Count > 0 ? Math.Floor(Convert.ToDouble(args[0])) : 0),

            ["ceil"] = new BuiltinFunctionValue("ceil", args =>
                args.Count > 0 ? Math.Ceiling(Convert.ToDouble(args[0])) : 0)
        };

    public static readonly IReadOnlyDictionary<string, BuiltinFunctionValue> TimeModule =
        new Dictionary<string, BuiltinFunctionValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["now"] = new BuiltinFunctionValue("now", _ => DateTime.UtcNow.ToString("o")),
            ["timestamp"] = new BuiltinFunctionValue("timestamp", _ => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            ["elapsed_ms"] = new BuiltinFunctionValue("elapsed_ms", args =>
            {
                if (args.Count == 0 || args[0] == null) return 0.0;
                string s = args[0]?.ToString() ?? "";
                if (DateTime.TryParse(s, out var dt))
                    return (DateTime.UtcNow - dt.ToUniversalTime()).TotalMilliseconds;
                if (long.TryParse(s, out var ms))
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
}
