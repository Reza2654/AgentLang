using System.Text.Json;
using AgentLang.AST;
using AgentLang.Runtime;
using AgentLang.Runtime.Values;
using AgentLang.Tools;
using AgentLang.VM;
using AgentLang.VM.Checkpointing;
using AgentLang.VM.MCP;
using AgentLang.VM.Observability;
using AgentLang.VM.Swarm;
using Xunit;

namespace AgentLang.Tests;

public class V065Tests
{
    [Fact]
    public async Task McpClient_And_McpServer_EndToEnd_Communication()
    {
        var (clientTransport, serverTransport) = InMemoryMcpTransport.CreatePair();

        var toolRegistry = new ToolRegistry();
        var server = new McpServer(serverTransport, toolRegistry);
        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(serverCts.Token));

        await using var client = new McpClient(clientTransport);
        try
        {
            await client.InitializeAsync("AgentLangClient", "0.6.5");
            Assert.True(client.IsInitialized);
            Assert.Equal("AgentLang-Server", client.ServerName);
            Assert.Equal("0.6.5", client.ServerVersion);

            // Discover tools
            var tools = await client.ListToolsAsync();
            Assert.NotEmpty(tools);
            Assert.Contains(tools, t => t.Name == "calculator");

            // Execute calculator tool via MCP
            var callResult = await client.CallToolAsync("calculator", new Dictionary<string, object?>
            {
                ["expression"] = "15 + 27"
            });

            Assert.False(callResult.IsError);
            Assert.NotEmpty(callResult.Content);
            Assert.Contains("42", callResult.Content[0].Text);
        }
        finally
        {
            serverCts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task McpClient_Bridges_Tools_To_AgentLang_Registry()
    {
        var (clientTransport, serverTransport) = InMemoryMcpTransport.CreatePair();

        var serverRegistry = new ToolRegistry();
        var server = new McpServer(serverTransport, serverRegistry);
        using var serverCts = new CancellationTokenSource();
        var serverTask = Task.Run(() => server.RunAsync(serverCts.Token));

        await using var client = new McpClient(clientTransport);
        try
        {
            await client.InitializeAsync();

            var localRegistry = new ToolRegistry();
            var bridges = await client.BridgeToolsToRegistryAsync(localRegistry, prefix: "ext");

            Assert.NotEmpty(bridges);
            Assert.Contains(bridges, b => b.Name == "mcp__ext_calculator");

            var bridgeTool = localRegistry.GetTool("mcp__ext_calculator");
            Assert.NotNull(bridgeTool);

            var invokeResult = await bridgeTool.ExecuteAsync(
                "mcp__ext_calculator",
                new Dictionary<string, object?> { ["expression"] = "100 / 4" });

            Assert.True(invokeResult.Success);
            Assert.Contains("25", invokeResult.Output?.ToString());
        }
        finally
        {
            serverCts.Cancel();
            try { await serverTask; } catch { }
        }
    }

    [Fact]
    public async Task ExecutionCheckpoint_Serialization_And_FilePersistence()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "agentlang_test_checkpoints_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileCheckpointStore(tempDir);

            var checkpoint = new ExecutionCheckpoint
            {
                WorkflowName = "FinancialAudit",
                StepIndex = 4,
                Status = "PausedWaitingApproval",
                StateVariables = new Dictionary<string, object?>
                {
                    ["account"] = "ACC-9921",
                    ["amount"] = 50000.0,
                    ["riskScore"] = 0.85
                },
                AgentHistories = new Dictionary<string, List<string>>
                {
                    ["Auditor"] = new List<string> { "user: Review transaction", "assistant: High risk flagged" }
                },
                Metadata = new Dictionary<string, string> { ["priority"] = "urgent" }
            };

            await store.SaveAsync(checkpoint);

            var loaded = await store.GetAsync(checkpoint.CheckpointId);
            Assert.NotNull(loaded);
            Assert.Equal("FinancialAudit", loaded.WorkflowName);
            Assert.Equal(4, loaded.StepIndex);
            Assert.Equal("PausedWaitingApproval", loaded.Status);
            Assert.True(loaded.StateVariables.ContainsKey("account"));

            var amountVal = loaded.StateVariables["amount"];
            double amount = amountVal switch
            {
                JsonElement elem => elem.GetDouble(),
                _ => Convert.ToDouble(amountVal)
            };
            Assert.Equal(50000.0, amount);

            var list = await store.ListAsync();
            Assert.Single(list);
            Assert.Equal(checkpoint.CheckpointId, list[0].CheckpointId);

            var deleted = await store.DeleteAsync(checkpoint.CheckpointId);
            Assert.True(deleted);

            var afterDelete = await store.GetAsync(checkpoint.CheckpointId);
            Assert.Null(afterDelete);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task CheckpointManager_Restores_VirtualMachine_State()
    {
        var vm = new AgentVirtualMachine();
        vm.Runtime.GlobalScope.SetLocal("threshold", 100);
        vm.Runtime.GlobalScope.SetLocal("status", "pending_review");

        // Create checkpoint
        var checkpoint = await vm.Checkpoints.CreateCheckpointAsync(
            vm,
            workflowName: "DeploymentPipeline",
            stepIndex: 3,
            status: "WaitingApproval");

        Assert.NotNull(checkpoint.CheckpointId);
        Assert.True(checkpoint.StateVariables.ContainsKey("threshold"));

        // Create fresh clean VM
        var newVm = new AgentVirtualMachine();
        Assert.Null(newVm.Runtime.GlobalScope.Get("threshold"));

        // Restore checkpoint into new VM
        bool restored = await vm.Checkpoints.RestoreCheckpointAsync(newVm, checkpoint.CheckpointId);
        Assert.True(restored);

        Assert.Equal(100, Convert.ToInt32(newVm.Runtime.GlobalScope.Get("threshold")));
        Assert.Equal("pending_review", newVm.Runtime.GlobalScope.Get("status")?.ToString());
    }

    [Fact]
    public async Task Swarm_Handoff_Delegation_And_Return_Synthesis()
    {
        var runtime = new AgentLangRuntime();

        // Register coordinator and specialist agents
        var coordinator = new AgentValue("Coordinator")
        {
            Role = "Support Coordinator",
            Instructions = "Direct questions to specialists and synthesize answers"
        };
        var specialist = new AgentValue("BillingSpecialist")
        {
            Role = "Billing Specialist",
            Instructions = "Resolve refund requests and invoice adjustments"
        };

        runtime.RegisterAgent(coordinator);
        runtime.RegisterAgent(specialist);

        var router = new SwarmHandoffRouter(runtime);
        var request = new SwarmHandoffRequest(
            FromAgent: "Coordinator",
            ToAgent: "BillingSpecialist",
            Reason: "Refund of $120 for invoice #4421",
            Payload: new { Invoice = "#4421", Amount = 120 },
            ReturnOnComplete: true);

        var result = await router.HandoffAsync(request);

        Assert.True(result.Success);
        Assert.NotEmpty(result.FinalResponse);
        Assert.Single(result.HandoffTrail);
        Assert.Equal("Coordinator", result.HandoffTrail[0].FromAgent);
        Assert.Equal("BillingSpecialist", result.HandoffTrail[0].ToAgent);
    }

    [Fact]
    public void Consensus_Engine_Majority_Weighted_Unanimous()
    {
        var votes = new List<AgentVote>
        {
            new("SecurityAgent", "Block", 0.95, "Brute force attack detected", Weight: 2.0),
            new("NetworkAgent", "Block", 0.80, "High traffic spike from single IP", Weight: 1.0),
            new("UserSupportAgent", "Allow", 0.60, "Could be customer forgot password", Weight: 1.0)
        };

        // 1. Majority
        var majority = AgentConsensusEngine.Tally(votes, VotingStrategy.Majority);
        Assert.True(majority.IsConsensusReached);
        Assert.Equal("Block", majority.Winner);

        // 2. Weighted (SecurityAgent has weight 2.0)
        var weighted = AgentConsensusEngine.Tally(votes, VotingStrategy.Weighted);
        Assert.True(weighted.IsConsensusReached);
        Assert.Equal("Block", weighted.Winner);
        Assert.True(weighted.VoteBreakdown["Block"] > weighted.VoteBreakdown["Allow"]);

        // 3. Unanimous (fails because UserSupportAgent voted Allow)
        var unanimous = AgentConsensusEngine.Tally(votes, VotingStrategy.Unanimous);
        Assert.False(unanimous.IsConsensusReached);
        Assert.Equal("Deadlock", unanimous.Winner);
    }

    [Fact]
    public async Task Debate_Protocol_MultiRound_Argument_Synthesis()
    {
        var runtime = new AgentLangRuntime();

        var proponent = new AgentValue("Advocate")
        {
            Role = "Microservices Architect",
            Instructions = "Argue in favor of microservices architecture"
        };
        var opponent = new AgentValue("Pragmatist")
        {
            Role = "Monolith Defender",
            Instructions = "Argue in favor of modular monoliths for early-stage startups"
        };
        var judge = new AgentValue("CTO")
        {
            Role = "Chief Technology Officer",
            Instructions = "Evaluate architectural arguments and deliver balanced verdict"
        };

        runtime.RegisterAgent(proponent);
        runtime.RegisterAgent(opponent);
        runtime.RegisterAgent(judge);

        var debateEngine = new DebateEngine();
        var debateResult = await debateEngine.ConductDebateAsync(
            runtime,
            proponentName: "Advocate",
            opponentName: "Pragmatist",
            judgeName: "CTO",
            topic: "Microservices vs Modular Monolith",
            maxRounds: 2);

        Assert.Equal(4, debateResult.Rounds.Count); // 2 rounds * 2 speakers
        Assert.Equal("Advocate", debateResult.Rounds[0].Speaker);
        Assert.Equal("Pragmatist", debateResult.Rounds[1].Speaker);
        Assert.Equal("CTO", debateResult.JudgeName);
        Assert.NotEmpty(debateResult.Verdict);
    }

    [Fact]
    public async Task OpenTelemetry_TraceContext_Propagation_And_ASCII_Export()
    {
        // 1. Context generation & parsing
        var ctx = TraceContext.CreateNew();
        Assert.Equal(32, ctx.TraceId.Length);
        Assert.Equal(16, ctx.SpanId.Length);

        var header = ctx.ToTraceParent();
        Assert.StartsWith("00-", header);

        var parsed = TraceContext.TryParse(header);
        Assert.NotNull(parsed);
        Assert.Equal(ctx.TraceId, parsed.TraceId);
        Assert.Equal(ctx.SpanId, parsed.SpanId);

        // 2. Tracing hierarchy
        var traceManager = new TraceManager();

        using (var rootSpan = traceManager.StartSpan("workflow.run", "Server"))
        {
            rootSpan.SetAttribute("workflow.name", "DataIngestion");

            using (var child1 = traceManager.StartSpan("agent.solve", "Internal"))
            {
                child1.SetAttribute("agent.name", "IngestWorker");
                child1.RecordTokens(400, 150, 0.0012);

                using (var toolSpan = traceManager.StartSpan("tool.call", "Client"))
                {
                    toolSpan.SetAttribute("tool.name", "http_get");
                }
            }

            using (var child2 = traceManager.StartSpan("actor.send", "Producer"))
            {
                child2.SetAttribute("actor.target", "StorageActor");
                child2.RecordTokens(200, 80, 0.0006);
            }
        }

        var completed = traceManager.CompletedSpans;
        Assert.Equal(4, completed.Count);

        // 3. Render ASCII Tree
        using var sw = new StringWriter();
        var consoleExporter = new ConsoleTraceExporter(sw);
        await traceManager.ExportAsync(consoleExporter);

        var output = sw.ToString();
        Assert.Contains("[Trace:", output);
        Assert.Contains("workflow.run", output);
        Assert.Contains("agent.solve", output);
        Assert.Contains("tool.call", output);
        Assert.Contains("actor.send", output);
        Assert.Contains("tokens", output);
    }
}
