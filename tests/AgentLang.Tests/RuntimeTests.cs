using AgentLang.AST;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Security;
using AgentLang.Tools;
using Xunit;

namespace AgentLang.Tests;

public class RuntimeTests
{
    private static (AgentLangRuntime runtime, StringWriter output) CreateTestRuntime()
    {
        var output = new StringWriter();
        var approver = new AutoApprovalProvider(true);
        var sec = new SecurityEngine(approver);
        var tools = new ToolRegistry(sec);
        var models = new ModelRegistry();
        var runtime = new AgentLangRuntime(
            modelRegistry: models,
            toolRegistry: tools,
            securityEngine: sec,
            output: output);

        return (runtime, output);
    }

    [Fact]
    public async Task ExecutesBasicPrintAndArithmetic()
    {
        string source = """
            main {
                x = 10
                y = 20
                sum = x + y
                print("Sum is: " + sum)
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        string consoleOutput = output.ToString();
        Assert.Contains("Sum is: 30", consoleOutput);
    }

    [Fact]
    public async Task ExecutesConditionalsAndLoops()
    {
        string source = """
            main {
                count = 0
                repeat 5 {
                    count = count + 1
                }
                if count == 5 {
                    print("Count reached 5")
                } else {
                    print("Failed")
                }
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Count reached 5", output.ToString());
    }

    [Fact]
    public async Task ExecutesAgentWithTaskAndDistinctOperationResult()
    {
        string source = """
            agent Researcher (
                model = GPT
            ) {
                context question = "What is AgentLang?"

                task research {
                    data = research(question)
                    answer = think(data.result)
                    return answer
                }

                print("Task result: " + research.result)
            }

            main {
                agent Researcher
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.True(runtime.AgentInstances.ContainsKey("Researcher"));
        var researcher = runtime.AgentInstances["Researcher"];
        Assert.True(researcher.Tasks.ContainsKey("research"));

        var task = researcher.Tasks["research"];
        Assert.Equal("completed", task.Status);
        Assert.NotNull(task.Result);

        // Result was returned as OperationValue from think()
        var op = Assert.IsType<OperationValue>(task.Result);
        Assert.Equal("think", op.OperationName);
        Assert.Equal("completed", op.Status);
        Assert.NotNull(op.Result);

        string consoleOutput = output.ToString();
        Assert.Contains("Task result: ", consoleOutput);
    }

    [Fact]
    public async Task MultiAgentCanAccessOtherAgentResults()
    {
        string source = """
            agent Researcher (model = GPT) {
                task research {
                    return "AI trends for 2026"
                }
            }

            agent Reviewer (model = Claude) {
                task review {
                    data = Researcher.research.result
                    return "Reviewed: " + data
                }
            }

            main {
                agent Researcher
                agent Reviewer
                print(Reviewer.review.result)
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Reviewed: AI trends for 2026", output.ToString());
    }

    [Fact]
    public async Task ExecutesParallelBlockConcurrently()
    {
        string source = """
            multiagent Team {
                parallel {
                    agent AgentA (model = GPT) {
                        task run { return "Result A" }
                    }
                    agent AgentB (model = Claude) {
                        task run { return "Result B" }
                    }
                }
            }

            main {
                agent AgentA
                agent AgentB
                print("A: " + AgentA.run.result + ", B: " + AgentB.run.result)
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("A: Result A, B: Result B", output.ToString());
    }

    [Fact]
    public async Task HandlesTryCatchAndRetry()
    {
        string source = """
            main {
                attempts = 0
                try {
                    retry 3 {
                        attempts = attempts + 1
                        if attempts < 3 {
                            x = 1 / 0
                        }
                    }
                    print("Retry succeeded on attempt: " + attempts)
                } catch err {
                    print("Caught error: " + err)
                }
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Retry succeeded on attempt: 3", output.ToString());
    }

    [Fact]
    public async Task DisablingMemoryLeavesMemoryEmpty()
    {
        string source = """
            agent StatelessAgent (
                model = GPT,
                memory = null
            ) {
                context testContext = "IgnoredMemory"
                task run {
                    return think("Hello")
                }
            }

            main {
                agent StatelessAgent
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, _) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        var agent = runtime.AgentInstances["StatelessAgent"];
        Assert.False(agent.MemoryEnabled);
        Assert.Empty(agent.Memory);
    }

    [Fact]
    public async Task EnforcesPermissionsInRuntime()
    {
        string source = """
            permission DenyBrowser {
                cannot browser.search
            }

            agent RestrictedAgent (
                model = GPT,
                permission = DenyBrowser
            ) {
                task testTask {
                    data = research("Test query")
                    return data
                }
            }

            main {
                agent RestrictedAgent
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var (runtime, _) = CreateTestRuntime();

        await Assert.ThrowsAsync<SecurityException>(() => runtime.ExecuteProgramAsync(program));
    }
}
