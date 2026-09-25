using AgentLang.AST;
using AgentLang.Errors;
using AgentLang.Models;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Memory;
using AgentLang.Runtime.Values;
using AgentLang.Security;
using AgentLang.Tools;
using Xunit;

namespace AgentLang.Tests;

public class RuntimeTests
{
    private static (AgentLangRuntime runtime, StringWriter output) CreateTestRuntime(TextReader? input = null, IPersistentMemoryStore? memoryStore = null)
    {
        var output = new StringWriter();
        var approver = new AutoApprovalProvider(true);
        var sec = new SecurityEngine(approver);
        var searchRegistry = new Tools.Search.SearchProviderRegistry(allowMockFallback: true);
        var tools = new ToolRegistry(sec, searchRegistry);
        var models = new ModelRegistry();
        var runtime = new AgentLangRuntime(
            modelRegistry: models,
            toolRegistry: tools,
            securityEngine: sec,
            memoryStore: memoryStore,
            output: output,
            input: input);

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

    [Fact]
    public async Task ExecutesFirstClassFunctionsAndTypeInspection()
    {
        string source = """
            function multiply(a, b) {
                return a * b
            }

            main {
                result = multiply(6, 7)
                resType = type(result)
                print("Result: " + result + ", Type: " + resType)
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Result: 42, Type: number", output.ToString());
    }

    [Fact]
    public async Task EnforcesRecursionStackLimit()
    {
        string source = """
            function infinite(n) {
                return infinite(n + 1)
            }

            main {
                infinite(1)
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, _) = CreateTestRuntime();

        var ex = await Assert.ThrowsAsync<AgentLangRuntimeException>(() => runtime.ExecuteProgramAsync(program));
        Assert.Equal("AGT301", ex.ErrorCode);
    }

    [Fact]
    public async Task ExecutesCustomToolDefinition()
    {
        string source = """
            tool greeter {
                description = "Custom greeter"
                input name: string
                execute {
                    return "Welcome, " + name
                }
            }

            main {
                msg = greeter("Agent")
                print(msg)
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Welcome, Agent", output.ToString());
    }

    [Fact]
    public async Task ExecutesAgentMessagingAndInbox()
    {
        string source = """
            agent Sender (model = mock) {
                task sendTask {
                    send "Task Payload" to Receiver tag "alert"
                }
            }

            agent Receiver (model = mock) {
                task receiveTask {
                    print("Inbox count: " + Receiver.inbox.length)
                    msg = Receiver.inbox[0]
                    print("Msg: " + msg.content + ", tag: " + msg.tag)
                }
            }

            main {
                agent Sender
                agent Receiver
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        var outputStr = output.ToString();
        Assert.Contains("Inbox count: 1", outputStr);
        Assert.Contains("Msg: Task Payload, tag: alert", outputStr);
        Assert.Single(runtime.AgentInstances["Receiver"].Inbox);
    }

    [Fact]
    public async Task ExecutesModelAliasesAndTaskOverrides()
    {
        string source = """
            model fast = mock

            agent AliasAgent (model = fast) {
                task t1 (model = fast) {
                    res = think("Testing alias")
                    print("Model: " + res.model)
                }
            }

            main {
                agent AliasAgent
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Model: mock", output.ToString());
        Assert.True(runtime.ModelRegistry.Aliases.ContainsKey("fast"));
    }

    [Fact]
    public async Task ExecutesPersistentMemoryStore()
    {
        var memStore = new InMemoryMemoryStore();
        string source1 = """
            agent MemoryUser (model = mock, memory = true) {
                context item = "remember_this_fact"
                task run {
                    print("Ran run")
                }
            }

            main {
                agent MemoryUser
            }
            """;

        var parser1 = new Parser.Parser(new SourceText(source1));
        var prog1 = parser1.ParseProgram();

        var output1 = new StringWriter();
        var runtime1 = new AgentLangRuntime(memoryStore: memStore, output: output1);
        await runtime1.ExecuteProgramAsync(prog1);

        var loaded = await memStore.LoadMemoryAsync("MemoryUser");
        Assert.NotEmpty(loaded);
        Assert.Contains(loaded, m => m.Contains("remember_this_fact"));
    }

    [Fact]
    public async Task ThrowsStructuredExceptionsWithErrorCode()
    {
        string source = """
            main {
                nums = [1, 2]
                val = nums[10]
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, _) = CreateTestRuntime();

        var ex = await Assert.ThrowsAsync<AgentLangRuntimeException>(() => runtime.ExecuteProgramAsync(program));
        Assert.Equal("AGT302", ex.ErrorCode);
    }

    [Fact]
    public async Task ImportApiConfiguresRuntimeProvidersAndEnvironment()
    {
        string source = """
            importapi gemini = "AIzaSyTestGeminiKey123", model = "gemini-1.5-flash"
            importapi tavily = "tvly-test-runtime-key"

            main {
                print("APIs configured successfully")
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("APIs configured successfully", output.ToString());
        Assert.Equal("AIzaSyTestGeminiKey123", Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        Assert.Equal("gemini-1.5-flash", Environment.GetEnvironmentVariable("GEMINI_MODEL"));
        Assert.Equal("tvly-test-runtime-key", Environment.GetEnvironmentVariable("TAVILY_API_KEY"));
    }

    private sealed class FailingTestModelProvider(string failModel) : IModelProvider
    {
        public string ProviderId => "failing-provider";
        public bool CanHandle(string modelName) => modelName.Equals(failModel, StringComparison.OrdinalIgnoreCase);
        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken ct = default) =>
            Task.FromResult(ModelResponse.Failed("Simulated API outage / rate limit", request.ModelName));
    }

    [Fact]
    public async Task ExecutesModelFallbackChain()
    {
        string source = """
            agent FallbackAgent (model = "non-existent-fail", fallback = "mock") {
                task testTask {
                    res = think("Testing fallback feature")
                    print("Answer: " + res)
                    print("Provider: " + res.model)
                }
            }

            main {
                agent FallbackAgent
            }
            """;

        var models = new ModelRegistry();
        var failingProvider = new FailingTestModelProvider("non-existent-fail");
        models.RegisterProvider(failingProvider);

        var output = new StringWriter();
        var sec = new SecurityEngine(new AutoApprovalProvider(true));
        var tools = new ToolRegistry(sec, new Tools.Search.SearchProviderRegistry(true));
        var runtime = new AgentLangRuntime(models, tools, sec, output: output);

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        await runtime.ExecuteProgramAsync(program);

        Assert.Contains("Answer: Analysis of 'Testing fallback feature'", output.ToString());
        Assert.Contains("Provider: mock", output.ToString());
    }

    [Fact]
    public async Task ExecutesPersonaGoalAndTemperature()
    {
        string source = """
            agent MathTutor (persona = "Math Genius", goal = "Accurate calculus", temperature = 0.2) {
                task testPersona {
                    print("Persona: " + MathTutor.persona)
                    print("Goal: " + MathTutor.goal)
                    print("Temp: " + MathTutor.temperature)
                    res = think("Calculate derivative")
                    print("Result: " + res)
                }
            }

            main {
                agent MathTutor
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Persona: Math Genius", result);
        Assert.Contains("Goal: Accurate calculus", result);
        Assert.Contains("Temp: 0.2", result);
    }

    [Fact]
    public async Task ExecutesRememberRecallForgetAndShortTermMemory()
    {
        var memStore = new InMemoryMemoryStore();
        string source = """
            agent MemoryBot (model = mock, memory = short_term) {
                task testMemory {
                    remember("User favorite color is blue")
                    remember("User age is 30")
                    remember("System framework is AgentLang")
                    
                    found = recall("color")
                    print("Recalled color: " + found[0])

                    countBefore = forget("User age")
                    print("Forgotten: " + countBefore)

                    all = recall("*")
                    print("Remaining count: " + all.length)
                }
            }

            main {
                agent MemoryBot
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime(memoryStore: memStore);

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Recalled color: User favorite color is blue", result);
        Assert.Contains("Forgotten: 1", result);
        Assert.Contains("Remaining count: 2", result);

        // Verify short_term did NOT save to persistent store
        var stored = await memStore.LoadMemoryAsync("MemoryBot");
        Assert.Empty(stored);
    }

    [Fact]
    public async Task ExecutesSwarmAndBroadcast()
    {
        string source = """
            agent Scout (model = mock) {
                task work {
                    print("Scout active")
                }
            }

            agent Analyst (model = mock) {
                task work {
                    print("Analyst active")
                }
            }

            swarm ResearchSwarm {
                agent Scout
                agent Analyst
                broadcast "Swarm synchronization event" with "sync"
            }

            main {
                swarm ResearchSwarm
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Scout active", result);
        Assert.Contains("Analyst active", result);

        Assert.True(runtime.AgentInstances.TryGetValue("Scout", out var scout));
        Assert.True(runtime.AgentInstances.TryGetValue("Analyst", out var analyst));
        Assert.Single(scout.Inbox);
        Assert.Equal("Swarm synchronization event", scout.Inbox[0].Content?.ToString());
        Assert.Equal("sync", scout.Inbox[0].Tag?.ToString());
        Assert.Single(analyst.Inbox);
    }

    [Fact]
    public async Task ExecutesDelegationBetweenAgents()
    {
        string source = """
            agent Specialist (model = mock) {
                context query = ""
                task solve {
                    res = think("Solution for " + query)
                    return res
                }
            }

            agent Coordinator (model = mock) {
                task coordinate {
                    answer = delegate "Deep learning optimization" to Specialist
                    print("Delegation reply: " + answer)
                }
            }

            main {
                agent Coordinator
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Delegation reply: Analysis of 'Solution for Deep learning optimization'", result);
    }

    [Fact]
    public async Task ExecutesPlanExpression()
    {
        string source = """
            agent Architect (model = mock) {
                task planTask {
                    p = plan "Build an autonomous rover"
                    print("Plan output: " + p)
                }
            }

            main {
                agent Architect
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Plan output: Plan for 'Build an autonomous rover'", result);
    }

    [Fact]
    public async Task ExecutesUntilLoop()
    {
        string source = """
            main {
                i = 0
                until i >= 3 {
                    i = i + 1
                    print("Iteration: " + i)
                }
                print("Final i: " + i)
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Iteration: 1", result);
        Assert.Contains("Iteration: 2", result);
        Assert.Contains("Iteration: 3", result);
        Assert.Contains("Final i: 3", result);
    }

    [Fact]
    public async Task ExecutesConfirmFunction()
    {
        string source = """
            main {
                approved = confirm("Deploy to production?")
                if (approved) {
                    print("Deployment approved")
                } else {
                    print("Deployment denied")
                }
            }
            """;

        // Test with "yes"
        var readerYes = new StringReader("yes\n");
        var (runtime1, output1) = CreateTestRuntime(input: readerYes);
        var program1 = new Parser.Parser(new SourceText(source)).ParseProgram();
        await runtime1.ExecuteProgramAsync(program1);
        Assert.Contains("Deployment approved", output1.ToString());

        // Test with "no"
        var readerNo = new StringReader("no\n");
        var (runtime2, output2) = CreateTestRuntime(input: readerNo);
        var program2 = new Parser.Parser(new SourceText(source)).ParseProgram();
        await runtime2.ExecuteProgramAsync(program2);
        Assert.Contains("Deployment denied", output2.ToString());
    }

    [Fact]
    public async Task ExecutesWaitAndAwait()
    {
        string source = """
            agent BackgroundWorker (model = mock) {
                task work {
                    print("Worker completed")
                }
            }

            main {
                wait 10
                await BackgroundWorker
                print("All done")
            }
            """;

        var parser = new Parser.Parser(new SourceText(source));
        var program = parser.ParseProgram();
        var (runtime, output) = CreateTestRuntime();

        await runtime.ExecuteProgramAsync(program);

        string result = output.ToString();
        Assert.Contains("Worker completed", result);
        Assert.Contains("All done", result);
    }
}
