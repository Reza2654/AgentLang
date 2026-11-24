using System.IO;
using System.Text.Json;

namespace AgentLang.VM.Lsp;

/// <summary>
/// Scaffolds and exports official VS Code extension files and TextMate grammar definition (.tmLanguage.json)
/// for syntax highlighting and Language Server Protocol integration.
/// </summary>
public static class VsCodeExtensionGenerator
{
    public static string GenerateTextMateGrammarJson()
    {
        var grammar = new
        {
            name = "AgentLang",
            scopeName = "source.agentlang",
            fileTypes = new[] { "agt", "agent" },
            patterns = new object[]
            {
                new { include = "#comments" },
                new { include = "#strings" },
                new { include = "#keywords" },
                new { include = "#types" },
                new { include = "#builtins" },
                new { include = "#numbers" },
                new { include = "#constants" }
            },
            repository = new
            {
                comments = new
                {
                    patterns = new object[]
                    {
                        new { match = @"//.*$", name = "comment.line.double-slash.agentlang" },
                        new { begin = @"/\*", end = @"\*/", name = "comment.block.agentlang" }
                    }
                },
                strings = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            begin = "\"",
                            end = "\"",
                            name = "string.quoted.double.agentlang",
                            patterns = new object[]
                            {
                                new { match = @"\\.", name = "constant.character.escape.agentlang" }
                            }
                        },
                        new
                        {
                            begin = "'",
                            end = "'",
                            name = "string.quoted.single.agentlang",
                            patterns = new object[]
                            {
                                new { match = @"\\.", name = "constant.character.escape.agentlang" }
                            }
                        }
                    }
                },
                keywords = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            match = @"\b(agent|schema|main|task|goal|model|role|instructions|instruction|tools|solve|print|if|else|while|for|in|return|try|catch|retry|emit|listen|swarm|debate|consensus|permission|allow|deny|dataset|train|import)\b",
                            name = "keyword.control.agentlang"
                        }
                    }
                },
                types = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            match = @"\b(string|int|float|double|bool|boolean|list|map|any)\b",
                            name = "entity.name.type.agentlang"
                        }
                    }
                },
                builtins = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            match = @"(@stdlib/(text|json|math|time|crypto|collections|rag|env)|text|json|math|time|crypto|collections|rag|env|data|db)\b",
                            name = "support.module.agentlang"
                        }
                    }
                },
                numbers = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            match = @"\b\d+(\.\d+)?\b",
                            name = "constant.numeric.agentlang"
                        }
                    }
                },
                constants = new
                {
                    patterns = new object[]
                    {
                        new
                        {
                            match = @"\b(true|false|null)\b",
                            name = "constant.language.agentlang"
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(grammar, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string GenerateLanguageConfigurationJson()
    {
        var config = new
        {
            comments = new
            {
                lineComment = "//",
                blockComment = new[] { "/*", "*/" }
            },
            brackets = new[]
            {
                new[] { "{", "}" },
                new[] { "[", "]" },
                new[] { "(", ")" }
            },
            autoClosingPairs = new object[]
            {
                new { open = "{", close = "}" },
                new { open = "[", close = "]" },
                new { open = "(", close = ")" },
                new { open = "\"", close = "\"", notIn = new[] { "string" } },
                new { open = "'", close = "'", notIn = new[] { "string", "comment" } }
            },
            surroundingPairs = new object[]
            {
                new { open = "{", close = "}" },
                new { open = "[", close = "]" },
                new { open = "(", close = ")" },
                new { open = "\"", close = "\"" },
                new { open = "'", close = "'" }
            }
        };

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string GeneratePackageJson(string version = "1.0.0")
    {
        var pkg = new
        {
            name = "agentlang-vscode",
            displayName = "AgentLang Support",
            description = "Official Language Server and Syntax Highlighting for AgentLang (.agt / .agent)",
            version,
            publisher = "reza2654",
            engines = new { vscode = "^1.85.0" },
            categories = new[] { "Programming Languages" },
            contributes = new
            {
                languages = new object[]
                {
                    new
                    {
                        id = "agentlang",
                        aliases = new[] { "AgentLang", "agentlang" },
                        extensions = new[] { ".agt", ".agent" },
                        configuration = "./language-configuration.json"
                    }
                },
                grammars = new object[]
                {
                    new
                    {
                        language = "agentlang",
                        scopeName = "source.agentlang",
                        path = "./syntaxes/agentlang.tmLanguage.json"
                    }
                }
            }
        };

        return JsonSerializer.Serialize(pkg, new JsonSerializerOptions { WriteIndented = true });
    }

    public static void ScaffoldExtension(string outputDirectory, string version = "1.0.0-rc.1")
    {
        Directory.CreateDirectory(outputDirectory);
        string syntaxesDir = Path.Combine(outputDirectory, "syntaxes");
        Directory.CreateDirectory(syntaxesDir);

        File.WriteAllText(Path.Combine(outputDirectory, "package.json"), GeneratePackageJson(version));
        File.WriteAllText(Path.Combine(outputDirectory, "language-configuration.json"), GenerateLanguageConfigurationJson());
        File.WriteAllText(Path.Combine(syntaxesDir, "agentlang.tmLanguage.json"), GenerateTextMateGrammarJson());
    }
}
