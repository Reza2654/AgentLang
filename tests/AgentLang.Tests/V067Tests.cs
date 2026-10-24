using AgentLang.VM;
using AgentLang.VM.Graphs;
using AgentLang.VM.Memory;
using AgentLang.VM.Schemas;
using AgentLang.VM.Streaming;
using Xunit;

namespace AgentLang.Tests;

public class V067Tests
{
    // =========================================================================
    // 1. STATE GRAPH ORCHESTRATION TESTS
    // =========================================================================

    [Fact]
    public async Task StateGraph_SequentialExecution_Succeeds()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("SequentialPipeline");

        graph.AddNode("step1", state =>
        {
            state["count"] = 1;
            return state;
        });

        graph.AddNode("step2", state =>
        {
            state["count"] = (int)state["count"]! + 10;
            return state;
        });

        graph.SetEntryPoint("step1");
        graph.AddEdge("step1", "step2");
        graph.SetFinishPoint("step2");

        var compiled = graph.Compile();
        var result = await compiled.InvokeAsync(new Dictionary<string, object?>());

        Assert.False(result.IsInterrupted);
        Assert.Equal(2, result.TotalSteps);
        Assert.Equal(11, result.State["count"]);
        Assert.Equal(2, result.ExecutionTrace.Count);
    }

    [Fact]
    public async Task StateGraph_ConditionalBranching_RoutesCorrectly()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("ClassifierGraph");

        graph.AddNode("classify", state =>
        {
            state["sentiment"] = ((string)state["input"]!).Contains("great") ? "positive" : "negative";
            return state;
        });

        graph.AddNode("handlePositive", state =>
        {
            state["outcome"] = "Promoted";
            return state;
        });

        graph.AddNode("handleNegative", state =>
        {
            state["outcome"] = "Escalated";
            return state;
        });

        graph.SetEntryPoint("classify");
        graph.AddConditionalEdge("classify", state => (string)state["sentiment"]!, new Dictionary<string, string>
        {
            ["positive"] = "handlePositive",
            ["negative"] = "handleNegative"
        });

        graph.SetFinishPoint("handlePositive");
        graph.SetFinishPoint("handleNegative");

        var compiled = graph.Compile();

        var positiveResult = await compiled.InvokeAsync(new Dictionary<string, object?> { ["input"] = "AgentLang is great!" });
        Assert.Equal("Promoted", positiveResult.State["outcome"]);

        var negativeResult = await compiled.InvokeAsync(new Dictionary<string, object?> { ["input"] = "This is a bug." });
        Assert.Equal("Escalated", negativeResult.State["outcome"]);
    }

    [Fact]
    public async Task StateGraph_CyclicRefinementLoop_TerminatesOnCondition()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("RefinementLoop");

        graph.AddNode("refine", state =>
        {
            int quality = state.TryGetValue("quality", out var q) ? (int)q! : 0;
            state["quality"] = quality + 3;
            return state;
        });

        graph.SetEntryPoint("refine");
        graph.AddConditionalEdge("refine", state =>
        {
            int q = (int)state["quality"]!;
            return q >= 9 ? StateGraphConstants.EndNode : "refine";
        });

        var compiled = graph.Compile();
        var result = await compiled.InvokeAsync(new Dictionary<string, object?>());

        Assert.Equal(9, result.State["quality"]);
        Assert.Equal(3, result.TotalSteps);
    }

    [Fact]
    public async Task StateGraph_InfiniteLoop_ThrowsCycleLimitException()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("InfiniteGraph");

        graph.AddNode("loopNode", state => state);
        graph.SetEntryPoint("loopNode");
        graph.AddEdge("loopNode", "loopNode"); // Infinite cycle

        var compiled = graph.Compile(new StateGraphOptions { MaxCycles = 5 });

        await Assert.ThrowsAsync<StateGraphCycleLimitException>(() =>
            compiled.InvokeAsync(new Dictionary<string, object?>()));
    }

    [Fact]
    public async Task StateGraph_InterruptAndResume_HumanInTheLoop()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("ApprovalGraph");

        graph.AddNode("draft", state =>
        {
            state["draft"] = "Payment of $500";
            return state;
        });

        graph.AddNode("executePayment", state =>
        {
            state["executed"] = true;
            return state;
        });

        graph.SetEntryPoint("draft");
        graph.AddEdge("draft", "executePayment");
        graph.SetFinishPoint("executePayment");

        // Interrupt before executing payment for human review
        graph.InterruptBefore("executePayment");

        var compiled = graph.Compile();

        // Step 1: Run until pause
        var pausedResult = await compiled.InvokeAsync(new Dictionary<string, object?>());
        Assert.True(pausedResult.IsInterrupted);
        Assert.Equal("executePayment", pausedResult.InterruptedNode);
        Assert.Equal("before", pausedResult.InterruptedPhase);
        Assert.False(pausedResult.State.ContainsKey("executed"));

        // Step 2: Human modifies or approves state and resumes
        pausedResult.State["approvedBy"] = "Admin";
        var resumedResult = await compiled.ResumeAsync(pausedResult.State);

        Assert.False(resumedResult.IsInterrupted);
        Assert.True((bool)resumedResult.State["executed"]!);
        Assert.Equal("Admin", resumedResult.State["approvedBy"]);
    }

    [Fact]
    public async Task StateGraph_ParallelBranches_MergesWithReducer()
    {
        var graph = new StateGraph<Dictionary<string, object?>>("FanOutGraph");

        graph.AddNode("startTask", state =>
        {
            state["input"] = "raw data";
            return state;
        });

        graph.AddNode("branchA", state =>
        {
            return new Dictionary<string, object?> { ["resultA"] = "processed A" };
        });

        graph.AddNode("branchB", state =>
        {
            return new Dictionary<string, object?> { ["resultB"] = "processed B" };
        });

        graph.SetEntryPoint("startTask");
        graph.AddParallelBranch("startTask", ["branchA", "branchB"], (parentState, branchStates) =>
        {
            foreach (var b in branchStates)
            {
                foreach (var (k, v) in b)
                {
                    parentState[k] = v;
                }
            }
            return parentState;
        });

        graph.SetFinishPoint("startTask");

        var compiled = graph.Compile();
        var result = await compiled.InvokeAsync(new Dictionary<string, object?>());

        Assert.Equal("processed A", result.State["resultA"]);
        Assert.Equal("processed B", result.State["resultB"]);
    }

    [Fact]
    public void StateGraph_ToMermaid_ProducesValidDiagram()
    {
        var graph = new StateGraph("TestFlow");
        graph.AddNode("stepA", s => s);
        graph.AddNode("stepB", s => s);
        graph.SetEntryPoint("stepA");
        graph.AddEdge("stepA", "stepB");
        graph.SetFinishPoint("stepB");

        string mermaid = graph.ToMermaid();
        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("__start__([Start]) --> stepA", mermaid);
        Assert.Contains("stepA --> stepB", mermaid);
        Assert.Contains("stepB --> __end__([End])", mermaid);
    }

    // =========================================================================
    // 2. EPISODIC & ENTITY GRAPH MEMORY TESTS
    // =========================================================================

    [Fact]
    public void EntityGraphMemory_AddAndQuery_Succeeds()
    {
        var mem = new EntityGraphMemory();

        mem.AddEntity("Alice", "Person", new() { ["role"] = "Architect" });
        mem.AddEntity("AgentLang", "Technology");
        mem.AddRelation("Alice", "created", "AgentLang", 1.0);

        Assert.Equal(2, mem.EntityCount);
        Assert.Equal(1, mem.RelationCount);

        var relations = mem.GetRelations("Alice");
        Assert.Single(relations);
        Assert.Equal("created", relations[0].Relation);
        Assert.Equal("AgentLang", relations[0].TargetId);
    }

    [Fact]
    public void EntityGraphMemory_ExtractAndStoreFacts_FromText()
    {
        var mem = new EntityGraphMemory();
        string text = "Reza created AgentLang. Alice works on Compiler. Bob likes AI.";

        int count = mem.ExtractAndStoreFacts(text);
        Assert.True(count >= 2);

        var aliceRelations = mem.GetRelations("Alice");
        Assert.NotEmpty(aliceRelations);
        Assert.Equal("works_on", aliceRelations[0].Relation);
    }

    [Fact]
    public void EntityGraphMemory_SubgraphQuery_TraversesRelationships()
    {
        var mem = new EntityGraphMemory();

        mem.AddEntity("A", "Node");
        mem.AddEntity("B", "Node");
        mem.AddEntity("C", "Node");

        mem.AddRelation("A", "connects_to", "B");
        mem.AddRelation("B", "connects_to", "C");

        var subgraph = mem.QuerySubgraph("A", maxDepth: 2);
        Assert.Equal(3, subgraph.Entities.Count);
        Assert.Equal(2, subgraph.Relations.Count);
    }

    [Fact]
    public void EntityGraphMemory_RecallContext_MatchesRelevantFacts()
    {
        var mem = new EntityGraphMemory();
        mem.AddEntity("Alice", "Person");
        mem.AddEntity("PostgreSQL", "Database");
        mem.AddEntity("Docker", "Container");

        mem.AddRelation("Alice", "uses", "PostgreSQL", 0.95);
        mem.AddRelation("Alice", "deploys", "Docker", 0.90);

        string context = mem.RecallContext("What database does Alice use?");
        Assert.Contains("ENTITY KNOWLEDGE GRAPH", context);
        Assert.Contains("Alice uses PostgreSQL", context);
    }

    [Fact]
    public void EntityGraphMemory_Serialization_RoundtripsSuccessfully()
    {
        var mem1 = new EntityGraphMemory();
        mem1.AddEntity("User1", "User", new() { ["tier"] = "Premium" });
        mem1.AddEntity("ServiceX", "Service");
        mem1.AddRelation("User1", "subscribed_to", "ServiceX");
        mem1.AddEpisode("User subscribed to ServiceX yesterday.");

        string json = mem1.ExportJson();
        Assert.NotEmpty(json);

        var mem2 = new EntityGraphMemory();
        mem2.ImportJson(json);

        Assert.Equal(mem1.EntityCount, mem2.EntityCount);
        Assert.Equal(mem1.RelationCount, mem2.RelationCount);
        Assert.Equal(mem1.EpisodeCount, mem2.EpisodeCount);
        Assert.NotNull(mem2.GetEntity("User1"));
    }

    // =========================================================================
    // 3. STRUCTURED SCHEMA VALIDATION & AUTO-REPAIR TESTS
    // =========================================================================

    [Fact]
    public void StructuredSchema_ToJsonSchema_ProducesStandardDraft()
    {
        var schema = SchemaBuilder.Create("UserAnalysis")
            .WithDescription("Structured user telemetry")
            .AddString("username", required: true)
            .AddInteger("score", required: true, min: 0, max: 100)
            .AddFloat("confidence", required: false, min: 0.0, max: 1.0)
            .AddBoolean("isVerified", required: true)
            .AddArray("tags", SchemaType.String)
            .Build();

        string jsonSchema = schema.ToJsonSchema();
        Assert.Contains("\"title\": \"UserAnalysis\"", jsonSchema);
        Assert.Contains("\"username\"", jsonSchema);
        Assert.Contains("\"minimum\": 0", jsonSchema);
        Assert.Contains("\"maximum\": 100", jsonSchema);
        Assert.Contains("\"required\"", jsonSchema);
    }

    [Fact]
    public void StructuredOutputParser_ValidatesCorrectJson()
    {
        var schema = SchemaBuilder.Create("Report")
            .AddString("title", required: true)
            .AddInteger("severity", required: true, min: 1, max: 5)
            .Build();

        string json = """
        ```json
        {
            "title": "Database connection drop",
            "severity": 4
        }
        ```
        """;

        var result = StructuredOutputParser.Validate(json, schema);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal("Database connection drop", result.Values!["title"]);
        Assert.Equal(4L, result.Values["severity"]);
    }

    [Fact]
    public void StructuredOutputParser_DetectsMissingAndConstraintViolations()
    {
        var schema = SchemaBuilder.Create("Report")
            .AddString("title", required: true)
            .AddInteger("severity", required: true, min: 1, max: 5)
            .Build();

        string invalidJson = """
        {
            "severity": 10
        }
        """;

        var result = StructuredOutputParser.Validate(invalidJson, schema);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Required field 'title' is missing"));
        Assert.Contains(result.Errors, e => e.Contains("exceeds maximum 5"));
    }

    [Fact]
    public void StructuredOutputParser_AutoRepair_FixesCommonLlmFlaws()
    {
        // Trailing comma, unquoted key, single quotes, missing closing brace
        string malformed = """
        ```json
        {
            name: 'AgentLang',
            version: '0.6.7',
            tags: ['ai', 'compiler',],
        """;

        string repaired = StructuredOutputParser.AutoRepair(malformed);
        Assert.DoesNotContain("'", repaired);
        Assert.Contains("\"name\":", repaired);
        Assert.EndsWith("}", repaired.Trim());

        // Ensure System.Text.Json can parse the repaired result
        var doc = System.Text.Json.JsonDocument.Parse(repaired);
        Assert.Equal("AgentLang", doc.RootElement.GetProperty("name").GetString());
    }

    // =========================================================================
    // 4. STREAMING ENGINE TESTS
    // =========================================================================

    [Fact]
    public async Task AgentStreamChannel_EmitsAndReadsEvents()
    {
        var channel = new AgentStreamChannel();

        _ = Task.Run(async () =>
        {
            await channel.EmitThoughtAsync("Analyzing request...", "Planner");
            await channel.EmitToolCallAsync("search", "{\"query\":\"AgentLang\"}", "Agent");
            await channel.EmitToolResultAsync("search", "Found 42 results", "Agent");
            await channel.EmitTokenAsync("Agent", "Agent");
            await channel.EmitTokenAsync("Lang", "Agent");
            await channel.EmitCompleteAsync("Done", tokens: 120, cost: 0.001);
            channel.Complete();
        });

        var events = new List<AgentStreamEvent>();
        await foreach (var ev in channel.ReadAllAsync())
        {
            events.Add(ev);
        }

        Assert.Equal(6, events.Count);
        Assert.Equal(AgentStreamEventType.Thought, events[0].Type);
        Assert.Equal(AgentStreamEventType.ToolCallStart, events[1].Type);
        Assert.Equal(AgentStreamEventType.ToolCallResult, events[2].Type);
        Assert.Equal(AgentStreamEventType.Token, events[3].Type);
        Assert.Equal(AgentStreamEventType.Token, events[4].Type);
        Assert.Equal(AgentStreamEventType.Complete, events[5].Type);

        string sse = events[0].ToSseString();
        Assert.StartsWith("event: Thought\ndata: ", sse);
    }

    [Fact]
    public async Task AgentVirtualMachine_StreamExecuteAsync_EmitsThoughtEvents()
    {
        var vm = new AgentVirtualMachine();
        var channel = new AgentStreamChannel();

        string code = """
        let x = 10;
        let y = 20;
        let sum = x + y;
        """;

        var collectedEvents = new List<AgentStreamEvent>();
        var readTask = Task.Run(async () =>
        {
            await foreach (var ev in channel.ReadAllAsync())
            {
                collectedEvents.Add(ev);
            }
        });

        string result = await vm.StreamExecuteAsync(code, channel, "test_stream.agt");
        await readTask;

        Assert.Equal("Completed", result);
        Assert.Contains(collectedEvents, e => e.Type == AgentStreamEventType.StatusChange && e.Content == "Initializing");
        Assert.Contains(collectedEvents, e => e.Type == AgentStreamEventType.Thought && e.Content.Contains("Step ["));
        Assert.Contains(collectedEvents, e => e.Type == AgentStreamEventType.Complete);
    }

    [Fact]
    public void AgentVirtualMachine_ExposesSubsystems()
    {
        var vm = new AgentVirtualMachine();
        Assert.NotNull(vm.EntityMemory);

        var graph = vm.CreateStateGraph("TestGraph");
        Assert.NotNull(graph);
        Assert.Equal("TestGraph", graph.Name);
    }
}
