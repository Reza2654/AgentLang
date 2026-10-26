using System.Net.Http.Json;
using System.Text.Json;
using AgentLang.VM;
using AgentLang.VM.Durable;
using AgentLang.VM.Server;
using Xunit;

namespace AgentLang.Tests;

public class V070Tests
{
    // =========================================================================
    // 1. DURABLE AGENT EXECUTION & SUSPENSION ENGINE TESTS
    // =========================================================================

    [Fact]
    public async Task DurableEngine_StartAndComplete_SetsState()
    {
        var engine = new DurableExecutionEngine(new MemoryDurableStateStore());

        var context = await engine.StartAsync(
            workflowName: "OrderProcessing",
            stepExecutor: stepCtx =>
            {
                stepCtx.SetVariable("orderId", "ORD-1234");
                stepCtx.SetVariable("amount", 250.0);
                stepCtx.Complete(new { processed = true });
                return Task.CompletedTask;
            },
            initialVars: new Dictionary<string, object?> { ["customer"] = "Alice" }
        );

        Assert.Equal("OrderProcessing", context.WorkflowName);
        Assert.Equal(DurableStatus.Completed, context.Status);
        Assert.Equal(1, context.StepIndex);
        Assert.Equal("Alice", context.StateVariables["customer"]?.ToString());
        Assert.Equal("ORD-1234", context.StateVariables["orderId"]?.ToString());
        Assert.NotNull(context.FinalOutput);

        // Verify state is stored
        var stored = await engine.GetAsync(context.ExecutionId);
        Assert.NotNull(stored);
        Assert.Equal(DurableStatus.Completed, stored.Status);
    }

    [Fact]
    public async Task DurableEngine_SuspendAndResume_PreservesStateAcrossSteps()
    {
        var engine = new DurableExecutionEngine(new MemoryDurableStateStore());

        // Step 1: Start and Suspend waiting for Human Approval
        var suspendedCtx = await engine.StartAsync(
            workflowName: "LoanApproval",
            stepExecutor: stepCtx =>
            {
                stepCtx.SetVariable("applicant", "Bob");
                stepCtx.SetVariable("creditScore", 740);
                // Suspend execution awaiting external risk approval signal
                stepCtx.Suspend(SuspensionReason.HumanApprovalRequired, "ManagerApproval");
                return Task.CompletedTask;
            }
        );

        Assert.Equal(DurableStatus.Suspended, suspendedCtx.Status);
        Assert.Equal(SuspensionReason.HumanApprovalRequired, suspendedCtx.SuspensionReason);
        Assert.Equal("ManagerApproval", suspendedCtx.WaitingForSignalName);
        Assert.Equal(1, suspendedCtx.StepIndex);

        // Attempt resume with wrong signal name -> should throw
        var wrongSignal = new ExecutionSignal("InvalidSignal", new { note = "wrong" });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await engine.ResumeAsync(suspendedCtx.ExecutionId, wrongSignal, _ => Task.CompletedTask);
        });

        // Step 2: Resume with correct signal
        var approvalSignal = new ExecutionSignal("ManagerApproval", new { approved = true, reviewer = "Carol" });
        var resumedCtx = await engine.ResumeAsync(
            suspendedCtx.ExecutionId,
            approvalSignal,
            stepCtx =>
            {
                // Verify state from previous step is intact
                var applicant = stepCtx.GetVariable<string>("applicant");
                Assert.Equal("Bob", applicant);

                // Read last received signal
                Assert.NotNull(stepCtx.LastSignal);
                Assert.Equal("ManagerApproval", stepCtx.LastSignal.SignalName);

                stepCtx.SetVariable("approvalStatus", "APPROVED");
                stepCtx.Complete(new { loanId = "LN-987", status = "GRANTED" });
                return Task.CompletedTask;
            }
        );

        Assert.Equal(DurableStatus.Completed, resumedCtx.Status);
        Assert.Null(resumedCtx.SuspensionReason);
        Assert.Null(resumedCtx.WaitingForSignalName);
        Assert.Equal(2, resumedCtx.StepIndex);
        Assert.Single(resumedCtx.ReceivedSignals);
        Assert.Equal("ManagerApproval", resumedCtx.ReceivedSignals[0].SignalName);
        Assert.Equal("APPROVED", resumedCtx.StateVariables["approvalStatus"]?.ToString());
    }

    [Fact]
    public async Task FileDurableStateStore_SavesAndReloadsFromDisk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "agentlang_durable_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileDurableStateStore(tempDir);
            var context = new DurableExecutionContext
            {
                WorkflowName = "DiskPersistTest",
                Status = DurableStatus.Suspended,
                SuspensionReason = SuspensionReason.ExternalWebhook,
                WaitingForSignalName = "StripeWebhook",
                StateVariables = new() { ["chargeId"] = "ch_test_123" }
            };

            await store.SaveAsync(context);

            // Fetch from store
            var reloaded = await store.GetAsync(context.ExecutionId);
            Assert.NotNull(reloaded);
            Assert.Equal("DiskPersistTest", reloaded.WorkflowName);
            Assert.Equal(DurableStatus.Suspended, reloaded.Status);
            Assert.Equal("StripeWebhook", reloaded.WaitingForSignalName);
            Assert.Equal("ch_test_123", reloaded.StateVariables["chargeId"]?.ToString());

            // List
            var list = await store.ListAsync(DurableStatus.Suspended);
            Assert.Single(list);

            // Delete
            bool deleted = await store.DeleteAsync(context.ExecutionId);
            Assert.True(deleted);

            var deletedCheck = await store.GetAsync(context.ExecutionId);
            Assert.Null(deletedCheck);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Vm_DurableEngine_IsInitializedAndAccessible()
    {
        var vm = new AgentVirtualMachine();
        Assert.NotNull(vm.Durable);
        Assert.NotNull(vm.Durable.Store);
    }

    // =========================================================================
    // 2. HTTP WEB MICROSERVICE GATEWAY TESTS
    // =========================================================================

    [Fact]
    public async Task AgentHttpServer_StatusAndPlaygroundEndpoints_ReturnOk()
    {
        int port = 9081;
        var vm = new AgentVirtualMachine();
        using var server = new AgentHttpServer(port, vm);
        using var cts = new CancellationTokenSource();

        await server.StartAsync(cts.Token);
        Assert.True(server.IsRunning);

        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

        try
        {
            // 1. GET /api/v1/status
            var statusResp = await client.GetAsync("/api/v1/status");
            Assert.True(statusResp.IsSuccessStatusCode);
            string statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal("0.7.0", doc.RootElement.GetProperty("version").GetString());

            // 2. GET /playground
            var playgroundResp = await client.GetAsync("/playground");
            Assert.True(playgroundResp.IsSuccessStatusCode);
            Assert.Equal("text/html; charset=utf-8", playgroundResp.Content.Headers.ContentType?.ToString());
            string html = await playgroundResp.Content.ReadAsStringAsync();
            Assert.Contains("AgentLang Playground", html);
            Assert.Contains("v0.7.0", html);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task AgentHttpServer_RunEndpoint_ExecutesCodeSuccessfully()
    {
        int port = 9082;
        var vm = new AgentVirtualMachine();
        using var server = new AgentHttpServer(port, vm);
        using var cts = new CancellationTokenSource();

        await server.StartAsync(cts.Token);

        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

        try
        {
            string script = """
            main {
                valA = 100
                valB = 25
                total = valA + valB
                print("Calculated Total is: " + total)
            }
            """;

            var requestBody = new
            {
                code = script,
                variables = new { initialParam = "sample" }
            };

            var response = await client.PostAsJsonAsync("/api/v1/run", requestBody);
            Assert.True(response.IsSuccessStatusCode);

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("completed", doc.RootElement.GetProperty("status").GetString());
            string output = doc.RootElement.GetProperty("output").GetString() ?? "";
            Assert.Contains("Calculated Total is: 125", output);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task AgentHttpServer_ResumeEndpoint_ResumesSuspendedWorkflow()
    {
        int port = 9083;
        var vm = new AgentVirtualMachine();

        // Create a suspended workflow in VM's durable engine
        var suspended = await vm.Durable.StartAsync(
            "ExpenseReport",
            stepCtx =>
            {
                stepCtx.SetVariable("amount", 450);
                stepCtx.Suspend(SuspensionReason.HumanApprovalRequired, "DirectorSignoff");
                return Task.CompletedTask;
            }
        );

        using var server = new AgentHttpServer(port, vm);
        using var cts = new CancellationTokenSource();

        await server.StartAsync(cts.Token);
        using var client = new HttpClient { BaseAddress = new Uri(server.BaseUrl) };

        try
        {
            var resumePayload = new
            {
                executionId = suspended.ExecutionId,
                signal = "DirectorSignoff",
                payload = new { approved = true, comments = "Budget verified" }
            };

            var response = await client.PostAsJsonAsync("/api/v1/resume", resumePayload);
            Assert.True(response.IsSuccessStatusCode);

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("Completed", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("stepIndex").GetInt32());
        }
        finally
        {
            server.Stop();
        }
    }
}
