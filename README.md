# AgentLang — v0.8.0

> **An Agent-Native Programming Language for Autonomous AI Systems**  
> *Built with modern C# 14 on .NET 10.*

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%2014-blue)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v0.8.0-success)](https://github.com/Reza2654/AgentLang/releases)
[![Tests](https://img.shields.io/badge/Tests-218%20Passing%20(100%25)-brightgreen)](https://github.com/Reza2654/AgentLang)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 🌟 What is AgentLang?

**AgentLang** is an agentic programming language designed from the ground up to make **Autonomous AI Agents**, **Multi-Agent Swarms**, **Bytecode IR (.agtc)**, **Hybrid Semantic Search**, **Auto-Remediation**, **Model Context Protocol (MCP)**, **Resumable Checkpoints**, and **Enterprise Guardrails** first-class language primitives.

Instead of writing hundreds of lines of fragile glue code in Python or TypeScript with string-interpolated prompts and unverified tool calls, AgentLang provides a **deterministic, compiler-checked, sandboxed DSL** with native file extension **`.agt`**.

```text
               AgentLang Script (.agt)
                         ↓
  Lexer ➔ Parser ➔ AST ➔ Semantic Analyzer
                         ↓
            ┌─────────────────────────┐
            │     AgentLang Runtime   │
            ├─────────────────────────┤
            │  • ReAct Engine         │
            │  • Memory & VectorStore │
            │  • Guardrails & Budget  │
            │  • Modular Imports      │
            │  • Swarm & Consensus    │
            │  • OpenAPI Tool Gen     │
            └────────────┬────────────┘
                         ↓
            ┌─────────────────────────┐
            │      AgentLang VM       │
            ├─────────────────────────┤
            │  • Bytecode Engine (.agtc)
            │  • Sandboxed Execution  │
            │  • Actor Model & Mailbox│
            │  • Context Compactor    │
            │  • Tool Rate Limiter    │
            │  • MCP Client & Server  │
            │  • State Checkpointing  │
            │  • Hybrid BM25 Search   │
            │  • Semantic Prompt Cache│
            │  • Self-Healing Loops   │
            │  • OpenTelemetry Tracing│
            │  • Eval & Benchmarks    │
            └─────────────────────────┘
```

---

## 🚀 Key Features

### 1. Agent Scripting DSL (`.agt`) & Top-Level Scripting
Define agents declaratively with model configurations, personas, strategies, tools, and memory stores. Top-level scripting allows running statements immediately without requiring boilerplates:

```agentlang
agent ResearchAssistant {
    model: "claude-3-5-sonnet"
    persona: "Rigorous research analyst focusing on empirical evidence"
    tools: [web_search, summarize_document]
    memory: ConversationBuffer(window: 5)
    strategy: ReAct(max_steps: 4)
}

goal task1 = "Summarize latest developments in Autonomous Agent VMs."
response = ResearchAssistant.achieve(task1)
print(response.output)
```

---

### 2. Multi-Agent Collaborative Pipelines (`pipeline`) & Evaluation Loops
Create multi-agent collaborative workflows with built-in convergence loops:

```agentlang
agent Writer {
    model: "gpt-4o"
    role: "Drafts comprehensive technical reports"
}

agent Critic {
    model: "gpt-4o"
    role: "Reviews technical accuracy and formatting"
}

pipeline ContentPipeline {
    input topic: string
    
    draft = Writer.draft(topic)
    
    loop until Critic.approves(draft) max_retries 3 {
        feedback = Critic.review(draft)
        draft = Writer.revise(draft, with: feedback)
    }
    
    return draft
}

result = ContentPipeline.run("Decentralized AI Agents")
print(result.output)
```

---

### 3. Shared State Management (`state`) & Team Workflows (`workflow`)
Orchestrate structured state transitions between multiple coordinating agents:

```agentlang
state FeatureState {
    spec: string = ""
    code: string = ""
    status: string = "PLANNING"
}

workflow BuildFeature(user_story: string) -> FeatureState {
    init state = FeatureState()

    state.spec = TechLead.execute("Design spec for: " + user_story)
    state.status = "CODING"

    loop attempts from 1 to 3 {
        state.code = Developer.execute(spec: state.spec)
        test_run = QAEngineer.run_tests(code: state.code)
        
        if (test_run.passed) {
            state.status = "DONE"
            break
        }
    }

    return state
}
```

---

### 4. Reactive Events & Semantic Decision-Making (`on event`, `decide`)
React asynchronously to events and logs using semantic reasoning and safety actions:

```agentlang
agent Sentinel {
    tools: [send_slack_alert, block_ip]

    on event(ServerLog log) {
        decide (log.content contains_suspicious_activity) {
            reasoning: "Analyze breach patterns and brute-force indicators"
            action {
                block_ip(log.ip)
                send_slack_alert(channel: "#security", message: "IP Blocked: " + log.ip)
            }
        }
    }
}
```

---

### 5. Enterprise Guardrails, Budgeting & Human-in-the-Loop
Enforce strict output schemas, forbidden patterns, and token/cost budgets at runtime:

```agentlang
agent FinancialAnalyst {
    model: "gpt-4o"
    
    guardrails {
        forbid: ["guaranteed profit", "financial advice"]
        require_format: Schema(summary: string, risks: list[string])
    }
}

budget max_tokens: 3000 max_cost: 0.05 {
    try {
        report = FinancialAnalyst.achieve("Quarterly fiscal audit")
    } catch (BudgetExceededError err) {
        print("Budget limit reached: " + err.message)
    } catch (GuardrailViolationError err) {
        transfer_to_human(reason: "Policy violation flagged")
    }
}
```

---

### 6. Agent Virtual Machine (`AgentLang.VM`)
The dedicated Agent Virtual Machine provides complete sandboxing and execution safety:
- **Instruction Cycle Limit**: Prevents infinite loops or runaway compute (`VmCycleLimitExceededException` / `VM101`).
- **Capability Gating**: Enforces zero-trust permissions on sensitive tools (`VmSandboxViolationException` / `VM102`).
- **State Checkpointing**: Create and restore snapshots of agent memory and global variables via `vm.CreateSnapshot()` and `vm.RestoreSnapshot()`.

```csharp
var options = new VmOptions
{
    MaxInstructions = 10_000,
    EnableSandboxing = true,
    AllowedCapabilities = ["memory.*"]
};

var vm = new AgentVirtualMachine(options);
await vm.ExecuteAsync(scriptCode);

var snapshot = vm.CreateSnapshot("checkpoint_alpha");
string json = snapshot.ToJson();
vm.RestoreSnapshot(snapshot);
```

---

### 7. Actor Model Concurrency (`AgentActorSystem`)
Isolated actor execution inspired by Erlang/BEAM:
- **Mailbox**: Non-blocking asynchronous message passing (`AskAsync`, `TellAsync`, `SendAsync`, `BroadcastAsync`).
- **Supervision Strategies**: Automatic fault recovery with `Restart`, `Resume`, or `Stop` policies.

```csharp
var actorSystem = new AgentActorSystem();
var worker = actorSystem.Spawn("calculator", async (self, msg) => {
    return (int)msg.Payload * 2;
}, strategy: SupervisionStrategy.Restart);

var result = await worker.AskAsync(21); // 42
```

---

### 8. Context Window Compaction & Rate Limiting
- **`ContextCompactor`**: Semantic summarization of older conversation turns into cohesive system memos while preserving recent context.
- **`TokenCache`**: Deterministic prompt completion and tool response caching with TTL and hit-rate tracking.
- **`ToolScheduler`**: Sliding-window rate limiter and concurrency semaphore preventing API provider rate-limits (HTTP 429).

---

### 9. Modular Imports & Standard Library (`@stdlib/*`)
Organize code into reusable packages:
```agentlang
import "./math_utils.agt";
import { sha256, uuid } from "@stdlib/crypto";
import { split, join, trim } from "@stdlib/text";
import { chunk_text, similarity } from "@stdlib/rag";

hash = sha256("AgentLang")
chunks = chunk_text("Autonomous AI agent system", 20, 5)
```
- Built-in circular import detection (`MOD002`).
- In-memory module caching.

---

### 10. Code Transpilation
Transpile AgentLang scripts into popular production AI ecosystems:
```bash
agent build app.agt --target python   # Emits clean LangGraph / CrewAI Python code
agent build app.agt --target ts       # Emits TypeScript code for Vercel AI SDK
```

---

### 11. Packaging & Docker Deployment
- **Manifest & Lockfile**: `agent.json` and `agent.lock` with SHA-256 integrity hashes.
- **Bundling**: `agent pack` packages your project into a `.agtpkg` distribution archive.
- **Docker Export**: `agent docker` generates optimized multi-stage production Dockerfile and `.dockerignore`.

---

### 12. Model Context Protocol (MCP) Client & Server
AgentLang provides native, standard-compliant Model Context Protocol (JSON-RPC 2.0) support:
- **`McpClient`**: Connect to external tool servers, discover tools dynamically (`tools/list`), and automatically bridge them into the agent tool registry (`mcp__<tool>`).
- **`McpServer`**: Run AgentLang as an MCP server over stdio, exposing your agents and workflows to Claude Desktop, Antigravity, or Cursor:
```bash
agent serve --mcp
```

---

### 13. Resumable State Checkpointing & Time-Travel Debugging
Long-running and human-in-the-loop workflows can be checkpointed and resumed across crashes or machine reboots:
- **`ExecutionCheckpoint`**: Snapshots variables, step indexes, agent conversation memories, and statuses.
- **Storage**: `FileCheckpointStore` persists `.agtchk` JSON checkpoints under `.agent/checkpoints/`.
```bash
agent checkpoint list
agent checkpoint show <id>
```

---

### 14. Swarm Protocols, Consensus Voting & Multi-Agent Debate
- **Dynamic Handoffs**: Transfer tasks between specialized agents with `ReturnOnComplete` synthesis.
- **Consensus Engine**: Multi-agent voting tally using `Majority`, `Weighted`, or `Unanimous` consensus.
- **Debate Protocol**: Multi-turn debates between opposing agents (e.g. Architect vs Pragmatist) with judge evaluation.

---

### 15. Distributed OpenTelemetry Tracing & Observability
W3C `traceparent` context propagation across agents, actor mailboxes, and tool calls. Visualize execution trees directly in the terminal:
```bash
agent run app.agt --trace
```
```text
[Trace: 88d6ba92f7123fb992a7bc07466f2661] Total: 67.7ms | Tokens: 484 | Cost: $0.0073
└── vm.program.execute [67.7ms]
    ├── agent.solve:ResearchAssistant [0.9ms] (120 tokens)
    ├── agent.solve:Writer [0.0ms] (120 tokens)
    ├── agent.solve:TechLead [0.0ms] (120 tokens)
    └── agent.solve:Developer [0.0ms] (124 tokens)
```
JSON traces are automatically exported to `.agent/traces/{traceId}.json`.

---

### 16. Bytecode Intermediate Representation (IR) & `.agtc` Compilation
Compile your AgentLang scripts into optimized, distributable stack-based bytecode binaries:
```bash
agent compile app.agt -o app.agtc
agent run app.agtc
```
- Faster startup and execution loop bypassing AST re-parsing.
- Compact binary format with constant pools, symbol tables, and instructions.

---

### 17. Hybrid Semantic Search & Semantic Prompt Caching
- **`HybridVectorStore`**: Combines Dense Vector Embeddings (cosine similarity) with Sparse Lexical Search (BM25) using **Reciprocal Rank Fusion (RRF)** for state-of-the-art document retrieval.
- **`SemanticPromptCache`**: Fuzzy semantic caching using cosine similarity threshold (default `0.80`), cutting redundant LLM API calls and costs.

---

### 18. Auto-Remediation & Self-Healing Loops
Never let a tool or API failure crash your autonomous agents:
- **`AutoRemediator`**: Executes fallback tool chains (`ToolFallbacks`) or triggers argument reflector repair (`ReflectorRepair`) to fix malformed inputs and retry execution autonomously.

---

### 19. Multi-Agent Evaluation & Benchmark Harness
Benchmark and evaluate agent accuracy, latency, and tool-calling precision:
```bash
agent eval app.agt --dataset test_suite.json --agent ResearchAssistant
```
Generates comprehensive Markdown scorecards with pass/fail ratios and regression metrics.

---

### 20. OpenAPI / Swagger Dynamic Tool Generator
Connect any REST service to AgentLang in seconds:
```csharp
OpenApiToolGenerator.RegisterOpenApiIntoRegistry(registry, openApiJson, "stripe_api");
```
Instantly converts OpenAPI 3.0 path endpoints into first-class callable AgentLang tools.

---

### 21. State Graph Orchestration (`StateGraph`) & Cyclic Workflows
Model complex multi-agent reasoning, iterative loops, and branch transitions as state graphs:
```csharp
var graph = new StateGraph("CustomerSupportPipeline");
graph.AddNode("triage", async (state, ct) => { ... });
graph.AddNode("billingHandler", async (state, ct) => { ... });
graph.AddConditionalEdge("triage", state => (string)state["intent"]);
graph.InterruptBefore("billingHandler"); // Human-in-the-loop review
graph.SetEntryPoint("triage");
var compiled = graph.Compile();
```
- **Cycle Safety**: Guards against runaway infinite execution with `MaxCycles`.
- **Fan-Out & Fan-In**: Parallel branch execution with custom state reducers.
- **Human-in-the-Loop (HITL)**: `InterruptBefore` and `InterruptAfter` allow inspection and state mutation before resuming with `ResumeAsync`.
- **Mermaid Export**: `graph.ToMermaid()` generates interactive flowcharts.

---

### 22. Episodic & Entity Graph Memory (`EntityGraphMemory`)
GraphRAG / Mem0-style long-term memory capturing knowledge relations:
- Stores entities, attributes, relations `(Source, Relation, Target)`, and episodic interaction history.
- **Auto Fact Extraction**: Extracts relationships from conversation text (e.g. `Reza created AgentLang`).
- **Contextual Recall**: `RecallContext(query)` dynamically recalls relevant entity relationships and injects structured context into agent prompts.
- **Multi-Hop Subgraph Traversals**: Explores connected entity subgraphs up to configurable depths.

---

### 23. Strict Structured Schema Engine & Auto-Repair (`StructuredSchema`)
Guarantees typed, schema-compliant JSON outputs from LLMs:
```csharp
var schema = SchemaBuilder.Create("AuditReport")
    .AddString("title", required: true)
    .AddInteger("vulnerabilitiesFound", required: true, min: 0)
    .AddArray("cveList", SchemaType.String)
    .Build();

var result = StructuredOutputParser.Validate(llmOutput, schema);
```
- Compiles standard JSON Schema Draft 2020-12 specifications (`ToJsonSchema()`).
- `AutoRepair` self-corrects malformed LLM outputs (fixes unquoted keys, single quotes, trailing commas, and unclosed braces/brackets).

---

### 24. Streaming Protocol & Real-Time Event Engine
Real-time token and lifecycle event streaming with W3C Server-Sent Events (SSE) support:
```bash
agent run app.agt --stream
```
- Emits real-time tokens, chain-of-thought steps, tool execution start/results, and status changes.
- Backpressure-managed asynchronous channels (`AgentStreamChannel`).
- Standard SSE format (`ToSseString()`) for plug-and-play web client integration.

---

### 25. Hierarchical Task Planning & ReWOO Engine (`AgentLang.VM.Planning`)
Decouple macro-strategy planning from tool execution to prevent context window bloat:
```bash
agent plan "Build full-text search indexing pipeline" --tools web_search,file_reader --execute
```
- **`PlanAndExecuteEngine`**: Generates directed task plans (`TaskPlan`), executing independent steps concurrently and dynamically triggering replanning (`ReplanAsync`) if an intermediate tool crashes.
- **`ReWooEngine` (Reasoning Without Observation)**: 3-phase execution (`Planner -> Worker -> Solver`) binding variable placeholders (`#E1`, `#E2`) without wasteful intermediate LLM reasoning tokens (saving up to 5x tokens).
- **`TreeOfThoughtsEngine`**: Multi-candidate Beam Search with scoring heuristics and path backtracking.

---

### 26. Multi-Modal Native Artifact Pipeline (`ArtifactStore`)
First-class storage for binary and structured data created by agents (datasets, charts, PDFs, reports):
```csharp
var art = vm.Artifacts.CreateTextArtifact("report.md", "# Analysis", "text/markdown");
var v2 = vm.Artifacts.UpdateArtifact(art.Id, updatedBytes);
```
- Content-addressed integrity using SHA-256 hashes.
- Lineage tracking with monotonic versioning and `ParentArtifactId`.
- Automatic persistence under `.agent/artifacts/`.

---

### 27. Data Loss Prevention (DLP) & Resource Access Boundaries
Enterprise-grade security controls preventing accidental credential leaks:
```csharp
string clean = DataLossPrevention.Sanitize(prompt, DlpAction.Redact);
```
- **Automated Detection**: Detects and sanitizes OpenAI/Anthropic API keys, GitHub tokens, AWS keys, private keys, credit cards, passwords, and emails.
- **Actions**: `Redact` (`[REDACTED:...]`), `Mask` (`sk-***123`), and `Block` (`DlpSecurityViolationException`).
- **Resource Boundary Defense**: Prevents directory traversal attacks (`../`) and enforces network domain whitelisting with wildcards (`*.github.com`).
- Run with `--dlp`: `agent run app.agt --dlp`.

---

### 28. Deterministic Testing Cassette System (VCR / Tool Replay)
Eliminate flaky agent tests and costly API bills during CI/CD:
```csharp
var recorder = new CassetteRecorder("regression_test", RecordMode.Replay);
string output = await recorder.ExecuteWithCassetteAsync("search_tool", args, realExecutor);
```
- Records real tool executions into versioned JSON cassettes (`.agent/cassettes/`).
- Replays recorded responses deterministically in offline CI pipelines with zero network overhead.

---

### 29. First-Class Native Schemas (`schema`)
Define type-safe structured data contracts and validation constraints natively in `.agt` code:
```agentlang
schema SecurityAuditReport {
    target: string,
    vulnerabilities_found: int(min: 0, max: 1000),
    risk_level: string(regex: "^(low|medium|high|critical)$"),
    passed: bool,
    cve_tags: [string]
}
```
- Direct keywords and syntax checking in compiler AST and Semantic Analyzer (`AL0038` duplicate detection).
- In-line constraints: `min`, `max`, `regex`, `required`, and array types `[T]`.
- Directly converts to VM `StructuredSchema` contracts for agent output parsing and auto-repair.

---

### 30. Smart Cascading LLM Gateway & Circuit Breaker
Eliminate rate-limit outages and provider downtimes with automatic failovers:
```csharp
var gateway = vm.Gateway;
gateway.FallbackChain.AddRange(["gpt-4o", "claude-3-5-sonnet", "llama3.2"]);
```
- **Cascading Fallbacks**: Seamlessly fails over from primary to secondary models upon transient errors or HTTP failures.
- **Provider Health Telemetry**: Live tracking of token consumption, request latency, and error frequencies.
- **Circuit Breaker**: Trips from `Closed` to `Open` after consecutive failures, shielding unresponsive endpoints, with automatic `HalfOpen` cooldown probing.

---

### 31. Native Offline Local LLM Execution (`Ollama`)
Deploy autonomous agents in air-gapped, high-privacy, or offline enterprise environments:
```agentlang
agent OfflineAuditor {
    model: "llama3.2"
    persona: "Local privacy-first code reviewer"
}
```
- Native `OllamaModelProvider` targeting local daemon endpoints (`/api/generate`).
- Seamless support for open-weights models: Llama 3, Mistral, DeepSeek, Qwen, Phi, Gemma, CodeLlama.
- Complete token count and generation telemetry without external cloud dependencies.

---

### 32. Durable Agent Execution & Suspension Engine (`AgentLang.VM.Durable`)
Orchestrate resilient, long-running agent workflows that survive process restarts and await external signals:
```csharp
var context = await vm.Durable.StartAsync("LoanApproval", stepCtx => {
    stepCtx.SetVariable("applicant", "Bob");
    // Pause execution awaiting external risk approval signal
    stepCtx.Suspend(SuspensionReason.HumanApprovalRequired, "ManagerApproval");
    return Task.CompletedTask;
});

// Resume at a later time upon receiving the signal
await vm.Durable.ResumeAsync(context.ExecutionId, new ExecutionSignal("ManagerApproval", payload), stepCtx => {
    stepCtx.Complete(new { status = "APPROVED" });
    return Task.CompletedTask;
});
```
- Complete state capture: step index, variables, agent histories, and received signals.
- Pluggable persistence (`MemoryDurableStateStore` and `FileDurableStateStore` in `.agent/durable/`).
- Signal-driven resumption without losing call stack or context.

---

### 33. HTTP Web Microservice Gateway & Interactive Playground (`agent serve --http`)
Expose AgentLang agents directly as high-performance REST & SSE microservices:
```bash
agent serve --http 8080
```
- **Endpoints**:
  - `POST /api/v1/run`: Execute `.agt` scripts with parameter injection and JSON outputs.
  - `POST /api/v1/stream`: Real-time Server-Sent Events (SSE) streaming for web clients and chat UIs.
  - `POST /api/v1/resume`: Wake up and resume suspended durable workflows with signal payloads.
  - `GET /api/v1/status`: Health check, active execution telemetry, and durable workflow overview.
- **Embedded Web Playground (`/` or `/playground`)**: Built-in dark-mode interactive dashboard for composing scripts, executing runs, streaming SSE events, and inspecting durable workflows in real time.

---

### 34. Language Server Protocol (LSP) & Official VS Code Extension
World-class developer tooling with real-time compilation feedback and editor intelligence:
```bash
agent lsp
# or
agent serve --lsp
```
- **LSP Capabilities**:
  - **Real-Time Diagnostics**: Instant feedback for syntax and semantic analyzer errors (`textDocument/publishDiagnostics`).
  - **IntelliSense Auto-Completion**: Context-aware keywords, schemas, agents, tools, and popular LLM models (`textDocument/completion`).
  - **Interactive Hover Documentation**: Rich markdown docstrings on hovering over keywords and declarations (`textDocument/hover`).
  - **Document Outline & Breadcrumbs**: Hierarchical symbol outline of agents, schemas, fields, and workflows (`textDocument/documentSymbol`).
- **Official VS Code Extension (`editors/vscode`)**:
  - TextMate grammar syntax highlighting (`.agt` and `.agent`).
  - Bracket matching, auto-closing pairs, and comment formatting.

---

## 🛠️ CLI Reference

| Command | Description |
| :--- | :--- |
| `agent run <file> [--trace] [--stream] [--dlp]` | Execute an AgentLang script with OpenTelemetry tracing, real-time event streaming, or DLP redaction |
| `agent serve [--mcp \| --http [port] \| --lsp]` | Start an MCP server, HTTP Web Gateway with Playground, or LSP language server |
| `agent lsp` | Start standard Language Server Protocol (LSP) server over stdio |
| `agent plan "<goal>" [--tools] [--execute]` | Generate or execute a hierarchical multi-step task plan DAG |
| `agent compile <file> [-o]` | Compile an AgentLang script into an optimized `.agtc` bytecode binary |
| `agent eval <file> --dataset`| Evaluate agent performance against a benchmark dataset |
| `agent checkpoint [list\|show]`| Inspect and list persistent workflow execution checkpoints |
| `agent chat <file> [agent]` | Interactive terminal chat with real-time thought traces |
| `agent repl` | Launch interactive REPL environment |
| `agent check [file]` | Static syntax and semantic verification |
| `agent format [file] [-w]` | Format AgentLang source code |
| `agent test [target]` | Run test suites |
| `agent build <file> [flags]` | Validate, transpile (`--target python\|ts`), or dockerize (`--docker`) |
| `agent pack [dir]` | Bundle project into distributable `.agtpkg` archive |
| `agent docker [dir] [entry]`| Generate production `Dockerfile` and `.dockerignore` |
| `agent new <name>` | Scaffold a new AgentLang project |
| `agent doctor` | Diagnose runtime, models, tools, and search providers |
| `agent install <pkg>` | Install a project dependency |

---

## 🧪 Testing & Verification

The test suite contains **218 automated tests (200 unit tests + 18 integration tests)** with a **100% pass rate**:

```bash
dotnet test tests/AgentLang.Tests/AgentLang.Tests.csproj --no-build
dotnet test tests/AgentLang.IntegrationTests/AgentLang.IntegrationTests.csproj --no-build
```

```text
Passed!  - Failed: 0, Passed: 200, Skipped: 0, Total: 200, Duration: 1.0 s - AgentLang.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  18, Skipped: 0, Total:  18, Duration: 1.0 s - AgentLang.IntegrationTests.dll (net10.0)
```

---

## 📄 License

AgentLang is licensed under the [MIT License](LICENSE).
