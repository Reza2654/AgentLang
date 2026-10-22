using AgentLang.Cli;
using AgentLang.Runtime.Values;
using AgentLang.VM.Actors;
using AgentLang.VM.Optimization;
using AgentLang.VM.Scheduling;
using Xunit;

namespace AgentLang.Tests;

public class V064Tests
{
    [Fact]
    public async Task ActorSystem_Spawns_Processes_Messages_Asynchronously()
    {
        var system = new AgentActorSystem();
        try
        {
            var actor = system.Spawn("calculator", async (self, msg) =>
            {
                if (msg.Payload is int n)
                {
                    return n * 2;
                }
                return msg.Payload?.ToString()?.ToUpperInvariant();
            });

            Assert.Equal("calculator", actor.Name);

            // Ask pattern
            var numResult = await actor.AskAsync(21);
            Assert.Equal(42, numResult);

            var strResult = await system.SendAsync("calculator", "agentlang");
            Assert.Equal("AGENTLANG", strResult);
        }
        finally
        {
            await system.ShutdownAsync();
        }
    }

    [Fact]
    public async Task ActorSystem_Supervision_Restarts_Faulted_Actor()
    {
        var system = new AgentActorSystem();
        try
        {
            int callCount = 0;
            var actor = system.Spawn(
                "fragile_worker",
                async (self, msg) =>
                {
                    callCount++;
                    if (msg.Payload is "crash")
                    {
                        throw new InvalidOperationException("Simulated crash");
                    }
                    return $"processed:{msg.Payload}";
                },
                strategy: SupervisionStrategy.Restart,
                maxRestarts: 3);

            // First call succeeds
            var r1 = await actor.AskAsync("first");
            Assert.Equal("processed:first", r1);

            // Second call crashes
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await actor.AskAsync("crash");
            });

            // Supervisor should have restarted the actor
            Assert.Equal(1, actor.RestartCount);

            // Third call succeeds after restart
            var r3 = await actor.AskAsync("after_restart");
            Assert.Equal("processed:after_restart", r3);
        }
        finally
        {
            await system.ShutdownAsync();
        }
    }

    [Fact]
    public void ContextCompactor_Compacts_Long_Conversation_History()
    {
        var history = new List<ChatMessage>();
        for (int i = 1; i <= 10; i++)
        {
            string role = i % 2 == 1 ? "user" : "assistant";
            history.Add(new ChatMessage(role, $"Conversation turn {i} with details and queries."));
        }

        Assert.Equal(10, history.Count);

        // Compact to keep max 5 messages, preserving 3 most recent
        var compacted = ContextCompactor.CompactHistory(history, maxMessages: 5, preserveRecent: 3);

        // Consolidated system memo (1) + preserved recent (3) = 4
        Assert.Equal(4, compacted.Count);
        Assert.Equal("system", compacted[0].Role);
        Assert.Contains("Context Compaction Memo", compacted[0].Content?.ToString());
        Assert.Contains("turn 1", compacted[0].Content?.ToString());
        Assert.Equal("Conversation turn 10 with details and queries.", compacted[^1].Content?.ToString());
    }

    [Fact]
    public void ContextCompactor_Estimates_Tokens_And_Prunes_Context()
    {
        string text = "This is a prompt with approximately forty characters.";
        int tokens = ContextCompactor.EstimateTokens(text);
        Assert.True(tokens > 8 && tokens < 25);

        string longContext = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}: Information log data {i}"));
        string pruned = ContextCompactor.PruneContext(longContext, maxTokens: 40);

        Assert.Contains("[... pruned earlier context ...]", pruned);
        Assert.True(ContextCompactor.EstimateTokens(pruned) <= 80);
    }

    [Fact]
    public async Task TokenCache_Caches_Values_Tracks_HitRate_And_TTL()
    {
        var cache = new TokenCache();

        cache.Set("prompt:123", "response_alpha", TimeSpan.FromSeconds(2));
        cache.Set("prompt:456", "response_beta");

        // Hit 1
        Assert.True(cache.TryGet("prompt:123", out var v1));
        Assert.Equal("response_alpha", v1);

        // Hit 2
        Assert.True(cache.TryGet("prompt:456", out var v2));
        Assert.Equal("response_beta", v2);

        // Miss 1
        Assert.False(cache.TryGet("prompt:999", out _));

        Assert.Equal(2, cache.Hits);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(0.6667, cache.HitRate);

        // Test clear
        cache.Clear();
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(0, cache.Misses);
    }

    [Fact]
    public async Task ToolScheduler_Queues_And_Enforces_Rate_Limits()
    {
        // 10 concurrent slots, 20 requests per second
        var scheduler = new ToolScheduler(maxConcurrency: 4, maxRequestsPerWindow: 20, windowSize: TimeSpan.FromSeconds(1));

        var tasks = new List<Task<int>>();
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            tasks.Add(scheduler.ScheduleAsync($"tool_task_{index}", async () =>
            {
                await Task.Delay(20);
                return index * 10;
            }));
        }

        var results = await Task.WhenAll(tasks);
        Assert.Equal(8, results.Length);
        Assert.Equal(0, results[0]);
        Assert.Equal(70, results[7]);
        Assert.Equal(8, scheduler.ExecutedCount);
        Assert.True(scheduler.PeakConcurrency <= 4);
    }

    [Fact]
    public void ProjectManager_Generates_Valid_Docker_Configuration()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AgentLangTest_Docker_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var (dockerfile, dockerignore) = ProjectManager.GenerateDockerFiles(tempDir, "runner.agt");

            Assert.True(File.Exists(dockerfile));
            Assert.True(File.Exists(dockerignore));

            string dfContent = File.ReadAllText(dockerfile);
            Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0", dfContent);
            Assert.Contains("runner.agt", dfContent);

            string diContent = File.ReadAllText(dockerignore);
            Assert.Contains(".git", diContent);
            Assert.Contains("*.agtpkg", diContent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
