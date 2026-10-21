using System.Security.Cryptography;
using System.Text;
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
            _ => throw new AgentLangRuntimeException($"Unknown standard library module: '{modulePath}'", errorCode: "STD001")
        };
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
