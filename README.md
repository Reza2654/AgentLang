# AgentLang — v0.6.4

> **An Agent-Native Programming Language for Autonomous AI Systems**  
> *Built with modern C# 14 on .NET 10.*

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%2014-blue)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v0.6.4-success)](https://github.com/Reza2654/AgentLang/releases)
[![Tests](https://img.shields.io/badge/Tests-125%20Passing%20(100%25)-brightgreen)](https://github.com/Reza2654/AgentLang)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 🌟 What is AgentLang?

**AgentLang** is an agentic programming language designed from the ground up to make **Autonomous AI Agents**, **Multi-Agent Swarms**, **Goal-Oriented Reasoning**, and **Enterprise Guardrails** first-class language primitives.

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
            └────────────┬────────────┘
                         ↓
            ┌─────────────────────────┐
            │      AgentLang VM       │
            ├─────────────────────────┤
            │  • Sandboxed Execution  │
            │  • Cycle Count Limits   │
            │  • Actor Model & Mailbox│
            │  • Context Compactor    │
            │  • Tool Scheduler       │
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

## 🛠️ CLI Reference

| Command | Description |
| :--- | :--- |
| `agent run <file>` | Execute an AgentLang script (`.agt` or `.agent`) |
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

The test suite contains **125 automated unit and integration tests** with a **100% pass rate**:

```bash
dotnet test tests/AgentLang.Tests/AgentLang.Tests.csproj --no-build
```

```text
Passed!  - Failed: 0, Passed: 125, Skipped: 0, Total: 125, Duration: 643 ms
```

---

## 📄 License

AgentLang is licensed under the [MIT License](LICENSE).
