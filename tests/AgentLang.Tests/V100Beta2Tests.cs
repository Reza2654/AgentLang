using System.IO;
using System.Text.Json;
using AgentLang.AST;
using AgentLang.Cli;
using AgentLang.Parser;
using AgentLang.Runtime.Stdlib;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Lsp;
using Xunit;

namespace AgentLang.Tests;

public class V100Beta2Tests
{
    [Fact]
    public void EnvModule_GetAndHas_ResolvesVariablesAndDefaults()
    {
        string testKey = "AGENTLANG_TEST_KEY_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(testKey, "Beta2Value");

        try
        {
            // 1. env.get existing
            StandardLibrary.TryInvoke("env", "get", new object?[] { testKey }, out var val1);
            Assert.Equal("Beta2Value", val1?.ToString());

            // 2. env.has existing
            StandardLibrary.TryInvoke("env", "has", new object?[] { testKey }, out var has1);
            Assert.True(has1 is bool b1 && b1);

            // 3. env.get non-existent with default
            StandardLibrary.TryInvoke("env", "get", new object?[] { "NON_EXISTENT_KEY_123", "FallbackVal" }, out var val2);
            Assert.Equal("FallbackVal", val2?.ToString());

            // 4. env.has non-existent
            StandardLibrary.TryInvoke("env", "has", new object?[] { "NON_EXISTENT_KEY_123" }, out var has2);
            Assert.False(has2 is bool b2 && b2);

            // 5. env.all contains dictionary
            StandardLibrary.TryInvoke("env", "all", Array.Empty<object?>(), out var allMap);
            Assert.NotNull(allMap);
            Assert.True(allMap is IDictionary<string, object?> dict && dict.ContainsKey(testKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(testKey, null);
        }
    }

    [Fact]
    public void Collections_UniqueAndSlice_HandleListsCorrectly()
    {
        // 1. unique
        var listWithDups = new List<object?> { "alpha", "beta", "alpha", "gamma", "beta" };
        StandardLibrary.TryInvoke("collections", "unique", new object?[] { listWithDups }, out var uniqueRes);
        Assert.NotNull(uniqueRes);
        var uniqueList = Assert.IsAssignableFrom<List<object?>>(uniqueRes);
        Assert.Equal(3, uniqueList.Count);
        Assert.Equal("alpha", uniqueList[0]);
        Assert.Equal("beta", uniqueList[1]);
        Assert.Equal("gamma", uniqueList[2]);

        // 2. slice
        var numbers = new List<object?> { 10, 20, 30, 40, 50 };
        StandardLibrary.TryInvoke("collections", "slice", new object?[] { numbers, 1, 3 }, out var sliceRes);
        Assert.NotNull(sliceRes);
        var sliceList = Assert.IsAssignableFrom<List<object?>>(sliceRes);
        Assert.Equal(3, sliceList.Count);
        Assert.Equal(20, sliceList[0]);
        Assert.Equal(30, sliceList[1]);
        Assert.Equal(40, sliceList[2]);

        // 3. text.index_of
        StandardLibrary.TryInvoke("text", "index_of", new object?[] { "autonomous agent", "agent" }, out var idx1);
        Assert.Equal(11, idx1);

        StandardLibrary.TryInvoke("text", "index_of", new object?[] { "autonomous agent", "missing" }, out var idx2);
        Assert.Equal(-1, idx2);
    }

    [Fact]
    public async Task BytecodeOptimizer_ConstantFolding_FoldsArithmeticAtCompileTime()
    {
        string source = """
            main {
                val = 15 + 25
                print(val)
            }
            """;

        var sourceText = new SourceText(source, "optimizer_test.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var compiler = new BytecodeCompiler();
        // Compile unoptimized first
        var unoptModule = compiler.Compile(program, "Unopt", optimize: false);
        int unoptInstCount = unoptModule.Instructions.Count;

        // Compile optimized
        var optModule = compiler.Compile(program, "Opt", optimize: true);
        int optInstCount = optModule.Instructions.Count;

        // Optimized module should have fewer instructions due to constant folding of 15 + 25 -> 40
        Assert.True(optInstCount < unoptInstCount, $"Expected optimized count ({optInstCount}) to be less than unoptimized ({unoptInstCount}).");

        // Verify that 40 (or 40L) is in the constant pool
        Assert.Contains(optModule.ConstantPool, c => c != null && (c.Equals(40L) || c.Equals(40.0) || c.Equals(40)));

        // Verify execution of optimized bytecode produces 40
        using var sw = new StringWriter();
        var vm = new AgentVirtualMachine(runtime: new Runtime.AgentLangRuntime(output: sw));
        var res = await vm.ExecuteBytecodeAsync(optModule);
        Assert.Contains("40", sw.ToString());
    }

    [Fact]
    public void VsCodeExtensionGenerator_GeneratesValidGrammarAndPackageJson()
    {
        // 1. TextMate Grammar JSON
        string grammarJson = VsCodeExtensionGenerator.GenerateTextMateGrammarJson();
        using var grammarDoc = JsonDocument.Parse(grammarJson);
        Assert.Equal("AgentLang", grammarDoc.RootElement.GetProperty("name").GetString());
        Assert.Equal("source.agentlang", grammarDoc.RootElement.GetProperty("scopeName").GetString());

        // 2. Language Configuration JSON
        string configJson = VsCodeExtensionGenerator.GenerateLanguageConfigurationJson();
        using var configDoc = JsonDocument.Parse(configJson);
        Assert.True(configDoc.RootElement.TryGetProperty("comments", out var comments));
        Assert.Equal("//", comments.GetProperty("lineComment").GetString());

        // 3. Package JSON
        string pkgJson = VsCodeExtensionGenerator.GeneratePackageJson("1.0.0-beta.2");
        using var pkgDoc = JsonDocument.Parse(pkgJson);
        Assert.Equal("agentlang-vscode", pkgDoc.RootElement.GetProperty("name").GetString());
        Assert.Equal("1.0.0-beta.2", pkgDoc.RootElement.GetProperty("version").GetString());

        // 4. Scaffold full directory
        string tempDir = Path.Combine(Path.GetTempPath(), $"vscode_ext_{Guid.NewGuid():N}");
        try
        {
            VsCodeExtensionGenerator.ScaffoldExtension(tempDir, "1.0.0-beta.2");
            Assert.True(File.Exists(Path.Combine(tempDir, "package.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "language-configuration.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "syntaxes", "agentlang.tmLanguage.json")));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Program_Version_Matches_V100BetaSeries()
    {
        Assert.StartsWith("1.0.0-beta", Program.Version);
    }
}
