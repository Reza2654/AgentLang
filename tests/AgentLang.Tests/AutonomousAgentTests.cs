using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.Tools;
using Xunit;

namespace AgentLang.Tests;

public class AutonomousAgentTests
{
    private static AgentLangRuntime CreateRuntime()
    {
        return new AgentLangRuntime();
    }

    [Fact]
    public async Task AgentDeclaration_BodyProperties_AreCorrectlyPopulated()
    {
        string code = """
        agent CodeAuditor {
            role: "Security Analyst"
            instructions: "Audit source files for vulnerabilities"
            model: "mock"
            tools: [filesystem, terminal]
            max_steps: 7
        }

        main {
            print(CodeAuditor.role)
            print(CodeAuditor.instructions)
            print(CodeAuditor.max_steps)
        }
        """;

        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);
        Assert.False(semantic.Diagnostics.HasErrors);

        var runtime = CreateRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.TryGetValue("CodeAuditor", out var agent));
        Assert.NotNull(agent);
        Assert.Equal("Security Analyst", agent.Role);
        Assert.Equal("Audit source files for vulnerabilities", agent.Instructions);
        Assert.Equal("mock", agent.Model);
        Assert.Equal(7, agent.MaxSteps);
        Assert.Contains("filesystem", agent.BoundTools);
        Assert.Contains("terminal", agent.BoundTools);
    }

    [Fact]
    public async Task AgentSolve_AutonomousReAct_ExecutesToolsAndReturnsAnswer()
    {
        string code = """
        agent Researcher {
            role: "Math Specialist"
            instructions: "Solve arithmetic accurately"
            model: "mock"
            tools: [calculator]
            max_steps: 5
        }

        main {
            res = Researcher.solve("calculate 25 + 75")
            print(res)
        }
        """;

        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = CreateRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.TryGetValue("Researcher", out var agent));
        Assert.NotNull(agent);
        Assert.True(runtime.GlobalScope.TryGet("res", out var resultObj));
        string resStr = resultObj?.ToString() ?? "";
        Assert.Contains("100", resStr);
    }

    [Fact]
    public async Task AgentSolve_FilesystemAnalysis_ExecutesReActToolLoop()
    {
        string code = """
        agent Inspector {
            role: "Filesystem Inspector"
            instructions: "Inspect target directories"
            model: "mock"
            tools: [filesystem]
            max_steps: 5
        }

        main {
            summary = Inspector.solve("Check files in folder src")
        }
        """;

        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = CreateRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.GlobalScope.TryGet("summary", out var sumObj));
        string summary = sumObj?.ToString() ?? "";
        Assert.NotEmpty(summary);
        Assert.Contains("بررسی فایل‌ها با موفقیت انجام شد", summary);
    }

    [Fact]
    public async Task AgentChat_MaintainsMultiTurnHistory()
    {
        string code = """
        agent Assistant {
            role: "Conversational Partner"
            instructions: "Be helpful and concise"
            model: "mock"
        }

        main {
            r1 = Assistant.chat("سلام")
            r2 = Assistant.chat("میشه کمکم کنی؟")
        }
        """;

        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = CreateRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.TryGetValue("Assistant", out var agent));
        Assert.NotNull(agent);
        // History should contain 2 user messages and 2 assistant messages = 4 messages
        Assert.Equal(4, agent.History.Count);
        Assert.Equal("user", agent.History[0].Role);
        Assert.Equal("assistant", agent.History[1].Role);
        Assert.Equal("user", agent.History[2].Role);
        Assert.Equal("assistant", agent.History[3].Role);
    }

    [Fact]
    public async Task AgentReset_ClearsSessionHistory()
    {
        string code = """
        agent Bot {
            role: "Bot"
            model: "mock"
        }

        main {
            Bot.chat("Message 1")
            Bot.reset()
            Bot.chat("Message 2")
        }
        """;

        var parser = new Parser.Parser(new SourceText(code));
        var program = parser.ParseProgram();
        Assert.False(parser.Diagnostics.HasErrors);

        var runtime = CreateRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.TryGetValue("Bot", out var agent));
        Assert.NotNull(agent);
        // After reset and Message 2, should only have 1 user and 1 assistant message = 2 messages
        Assert.Equal(2, agent.History.Count);
        Assert.Equal("Message 2", agent.History[0].Content);
    }

    [Fact]
    public async Task AgentSolve_OfflineCodeBugFixing_AnalyzesAndFixesCodeOnDisk()
    {
        string testDir = "test_dir_23";
        if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        Directory.CreateDirectory(testDir);

        string testFilePath = Path.Combine(testDir, "calculator.py");
        string buggyCode = """
        def divide(a, b):
            return a / b

        prin("Testing calculator...")
        result = divide(10, 0)
        """;
        await File.WriteAllTextAsync(testFilePath, buggyCode);

        try
        {
            string agentLangScript = $$"""
            agent CodeFixer {
                role: "Code Debugger"
                instructions: "Fix code bugs in target directory"
                model: "mock"
                tools: [filesystem]
                max_steps: 6
            }

            main {
                report = CodeFixer.solve("برو پوشه {{testDir}} رو باز کن، کد توش رو تحلیل کن و بعد بیا باگشو برطرف کن بعد بیا به من باگ رو بگه")
            }
            """;

            var parser = new Parser.Parser(new SourceText(agentLangScript));
            var program = parser.ParseProgram();
            Assert.False(parser.Diagnostics.HasErrors);

            var runtime = CreateRuntime();
            await runtime.ExecuteProgramAsync(program);

            Assert.True(runtime.GlobalScope.TryGet("report", out var repObj));
            string report = repObj?.ToString() ?? "";
            Assert.NotEmpty(report);

            // Assert report contains bug details
            Assert.Contains("تقسیم بر صفر", report);
            Assert.Contains("prin", report);
            Assert.Contains("اصلاح", report);

            // Assert file on disk was modified and fixed
            string updatedCode = await File.ReadAllTextAsync(testFilePath);
            Assert.DoesNotContain("prin(", updatedCode);
            Assert.Contains("print(", updatedCode);
            Assert.DoesNotContain("divide(10, 0)", updatedCode);
            Assert.Contains("divide(10, 2)", updatedCode);
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }
}
