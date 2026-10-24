using System.Text;
using AgentLang.Security.Policy;
using AgentLang.VM;
using AgentLang.VM.Artifacts;
using AgentLang.VM.Planning;
using AgentLang.VM.Testing;
using Xunit;

namespace AgentLang.Tests;

public class V068Tests
{
    // =========================================================================
    // 1. PLAN-AND-EXECUTE & REWOO & TREE OF THOUGHTS TESTS
    // =========================================================================

    [Fact]
    public async Task PlanAndExecute_SequentialSteps_CompletesSuccessfully()
    {
        var planner = new HeuristicPlanner();
        var engine = new PlanAndExecuteEngine(planner);

        var executedSteps = new List<int>();
        var result = await engine.ExecuteAsync(
            "Search for latest autonomous agent benchmarks",
            async (step, ct) =>
            {
                executedSteps.Add(step.StepId);
                await Task.Yield();
                return $"Processed step {step.StepId}";
            },
            availableTools: ["web_search", "document_summary"]
        );

        Assert.True(result.Plan.IsComplete);
        Assert.Equal(3, result.Plan.Steps.Count);
        Assert.Equal(3, executedSteps.Count);
        Assert.Equal(0, result.ReplansCount);
        Assert.Contains("successfully achieved", result.Summary);
    }

    [Fact]
    public async Task PlanAndExecute_OnFailure_TriggersReplanning()
    {
        var planner = new HeuristicPlanner();
        var engine = new PlanAndExecuteEngine(planner)
        {
            EnableReplanning = true,
            MaxReplans = 2
        };

        bool hasFailedOnce = false;
        var result = await engine.ExecuteAsync(
            "Standard business process",
            async (step, ct) =>
            {
                if (step.StepId == 2 && !hasFailedOnce)
                {
                    hasFailedOnce = true;
                    throw new InvalidOperationException("Simulated transient tool crash");
                }
                await Task.Yield();
                return $"Success for step {step.StepId}";
            }
        );

        Assert.True(result.Plan.IsComplete);
        Assert.Equal(1, result.ReplansCount);
        Assert.Contains("[Remediated: alternative approach]", result.Plan.Steps.First(s => s.StepId == 2).Description);
    }

    [Fact]
    public async Task ReWooEngine_SubstitutesEvidencePlaceholders()
    {
        var engine = new ReWooEngine();

        var plan = new ReWooPlan("Analyze repository metrics",
        [
            new ReWooStep("#E1", "Get commit count", "git_tool", new() { ["action"] = "commits" }),
            new ReWooStep("#E2", "Get contributor count", "git_tool", new() { ["action"] = "contributors" }),
            new ReWooStep("#E3", "Summarize findings", "summary_tool", new() { ["input"] = "Commits: #E1, Contributors: #E2" })
        ]);

        var executedCalls = new List<(string Tool, string Input)>();

        var result = await engine.ExecuteAsync(
            plan,
            toolExecutor: (tool, args, ct) =>
            {
                string argValue = args.Values.FirstOrDefault() ?? "";
                executedCalls.Add((tool, argValue));

                if (argValue == "commits") return Task.FromResult("1540");
                if (argValue == "contributors") return Task.FromResult("48");
                if (argValue.Contains("Commits: 1540")) return Task.FromResult("Healthy repo with 1540 commits and 48 contributors.");

                return Task.FromResult("Unknown");
            }
        );

        Assert.Equal(3, result.Evidence.Count);
        Assert.Equal("1540", result.Evidence["#E1"]);
        Assert.Equal("48", result.Evidence["#E2"]);
        Assert.Contains("Healthy repo with 1540 commits and 48 contributors.", result.Evidence["#E3"]);
        Assert.Contains("Healthy repo with 1540 commits and 48 contributors.", result.FinalOutput);
    }

    [Fact]
    public async Task TreeOfThoughtsEngine_BeamSearch_FindsOptimalPath()
    {
        var engine = new TreeOfThoughtsEngine
        {
            MaxDepth = 3,
            BeamWidth = 2,
            PruningThreshold = 0.50
        };

        var path = await engine.SearchAsync(
            "Root Challenge",
            thoughtGenerator: (node, ct) =>
            {
                // Generate 3 candidate hypotheses at each step
                return Task.FromResult(new List<string>
                {
                    $"{node.Content} -> Branch A",
                    $"{node.Content} -> Branch B",
                    $"{node.Content} -> Branch C"
                });
            },
            thoughtEvaluator: (node, candidate, ct) =>
            {
                // Heuristic score: favors Branch B paths
                double score = candidate.Contains("Branch B") ? 0.90 : 0.45;
                return Task.FromResult(score);
            }
        );

        Assert.NotEmpty(path);
        Assert.Equal(4, path.Count); // Root + 3 depths
        Assert.Equal("Root Challenge", path[0].Content);
        Assert.Contains("Branch B", path[^1].Content);
        Assert.True(path[^1].Score >= 0.80);
    }

    // =========================================================================
    // 2. MULTI-MODAL ARTIFACT PIPELINE TESTS
    // =========================================================================

    [Fact]
    public void ArtifactStore_CreateAndQueryArtifacts_Succeeds()
    {
        var store = new ArtifactStore();

        var textArt = store.CreateTextArtifact("report.md", "# Performance Analysis\nAll systems operational.", "text/markdown");
        Assert.NotNull(textArt.Id);
        Assert.StartsWith("art_", textArt.Id);
        Assert.Equal("report.md", textArt.Name);
        Assert.Equal("text/markdown", textArt.MimeType);
        Assert.NotEmpty(textArt.Sha256);
        Assert.Equal(1, textArt.Version);
        Assert.Contains("Performance Analysis", textArt.TextContent);

        var retrieved = store.GetArtifact(textArt.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(textArt.Sha256, retrieved.Sha256);
    }

    [Fact]
    public void ArtifactStore_VersionUpdating_MaintainsLineage()
    {
        var store = new ArtifactStore();

        var v1 = store.CreateTextArtifact("chart.csv", "A,B\n1,2", "text/csv");
        Assert.Equal(1, v1.Version);

        var v2 = store.UpdateArtifact(v1.Id, Encoding.UTF8.GetBytes("A,B\n1,2\n3,4"));
        Assert.Equal(2, v2.Version);
        Assert.Equal(v1.Id, v2.ParentArtifactId);
        Assert.NotEqual(v1.Sha256, v2.Sha256);

        var latest = store.GetArtifactByName("chart.csv");
        Assert.NotNull(latest);
        Assert.Equal(2, latest.Version);

        var explicitV1 = store.GetArtifactByName("chart.csv", version: 1);
        Assert.NotNull(explicitV1);
        Assert.Equal(1, explicitV1.Version);
    }

    [Fact]
    public void ArtifactStore_JsonArtifact_SerializesProperly()
    {
        var store = new ArtifactStore();
        var data = new { Host = "localhost", Port = 5000, Status = "Healthy" };

        var art = store.CreateJsonArtifact("status.json", data);
        Assert.Equal("application/json", art.MimeType);
        Assert.Contains("\"Status\": \"Healthy\"", art.TextContent);
    }

    // =========================================================================
    // 3. DATA LOSS PREVENTION & SECURITY POLICY TESTS
    // =========================================================================

    [Fact]
    public void DataLossPrevention_Scan_IdentifiesCredentials()
    {
        string prompt = "Connect using sk-1234567890abcdef1234567890 and email admin@company.com with token Bearer abcdef1234567890abcdef1234567890";

        var matches = DataLossPrevention.Scan(prompt);
        Assert.True(matches.Count >= 2);
        Assert.Contains(matches, m => m.RuleName == "OpenAI_ApiKey");
        Assert.Contains(matches, m => m.RuleName == "Email_Address");
    }

    [Fact]
    public void DataLossPrevention_Redact_ReplacesWithPlaceholder()
    {
        string prompt = "Secret key: sk-abcdef1234567890abcdef1234567890";
        string sanitized = DataLossPrevention.Sanitize(prompt, DlpAction.Redact);

        Assert.DoesNotContain("sk-abcdef", sanitized);
        Assert.Contains("[REDACTED:OpenAI_ApiKey]", sanitized);
    }

    [Fact]
    public void DataLossPrevention_Mask_HidesInnerCharacters()
    {
        string prompt = "Card: 4532-1234-5678-9010";
        string masked = DataLossPrevention.Sanitize(prompt, DlpAction.Mask);

        Assert.DoesNotContain("1234-5678", masked);
        Assert.StartsWith("Card: 45", masked);
        Assert.EndsWith("10", masked);
    }

    [Fact]
    public void DataLossPrevention_BlockAction_ThrowsOnCriticalSecret()
    {
        string prompt = "GitHub token: ghp_1234567890abcdef1234567890abcdef123456";

        Assert.Throws<DlpSecurityViolationException>(() =>
            DataLossPrevention.Sanitize(prompt, DlpAction.Block));
    }

    [Fact]
    public void ResourceAccessPolicy_PathAndDomainEnforcement()
    {
        var policy = new ResourceAccessPolicy();
        policy.AllowedPathRoots.Add(@"C:\safe\workspace");
        policy.AllowedDomains.Add("*.github.com");
        policy.AllowedDomains.Add("api.weather.com");

        // Paths
        Assert.True(policy.IsPathAllowed(@"C:\safe\workspace\data.json", @"C:\safe\workspace"));
        Assert.False(policy.IsPathAllowed(@"C:\Windows\System32\cmd.exe", @"C:\safe\workspace"));
        Assert.False(policy.IsPathAllowed(@"C:\safe\workspace\..\..\secret.txt", @"C:\safe\workspace"));

        // Domains
        Assert.True(policy.IsDomainAllowed("api.github.com"));
        Assert.True(policy.IsDomainAllowed("https://raw.github.com/data"));
        Assert.True(policy.IsDomainAllowed("api.weather.com"));
        Assert.False(policy.IsDomainAllowed("evil-hacker.com"));
    }

    // =========================================================================
    // 4. DETERMINISTIC TESTING CASSETTE (VCR) TESTS
    // =========================================================================

    [Fact]
    public async Task CassetteRecorder_RecordAndReplay_DeterministicOutputs()
    {
        var cassette = new ToolCassette("unit_test");
        var recorder = new CassetteRecorder { Cassette = cassette, Mode = RecordMode.Record };

        int realCallCounter = 0;
        Func<CancellationToken, Task<string>> executor = ct =>
        {
            realCallCounter++;
            return Task.FromResult($"Output #{realCallCounter}");
        };

        // 1. Record mode: executes real tool
        string res1 = await recorder.ExecuteWithCassetteAsync("fetch_stock", "{\"symbol\":\"MSFT\"}", executor);
        Assert.Equal("Output #1", res1);
        Assert.Equal(1, realCallCounter);
        Assert.Single(cassette.Interactions);

        // 2. Switch to Replay mode: serves from cassette without touching real executor
        recorder.Mode = RecordMode.Replay;
        string res2 = await recorder.ExecuteWithCassetteAsync("fetch_stock", "{\"symbol\":\"MSFT\"}", executor);
        Assert.Equal("Output #1", res2);
        Assert.Equal(1, realCallCounter); // Counter did NOT increase!

        // 3. Unrecorded interaction in replay mode throws
        await Assert.ThrowsAsync<CassetteInteractionNotFoundException>(() =>
            recorder.ExecuteWithCassetteAsync("fetch_stock", "{\"symbol\":\"AAPL\"}", executor));
    }

    [Fact]
    public void AgentVirtualMachine_ExposesV068Subsystems()
    {
        var vm = new AgentVirtualMachine();

        Assert.NotNull(vm.Artifacts);
        Assert.NotNull(vm.Planner);
        Assert.NotNull(vm.ReWoo);
        Assert.NotNull(vm.TreeOfThoughts);
        Assert.NotNull(vm.ResourcePolicy);
        Assert.NotNull(vm.CassetteRecorder);
    }
}
