# AgentLang — v0.2.0

> **An Agent-Native Programming Language for Autonomous AI Systems**  
> *Built with modern C# 14 on .NET 10.*

[![Build and Test](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%2014-blue)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Version](https://img.shields.io/badge/Release-v0.2.0-blue)](https://github.com/Reza2654/AgentLang/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 1. What is AgentLang?

**AgentLang** is a programming language designed from the ground up to make **AI Agents** first-class programming constructs.

Instead of writing complex glue code across multiple Python libraries, prompt templates, and brittle API wrappers, AgentLang provides a cohesive, deterministic, and secure syntax where **Agents, Tasks, Models, Tools, Permissions, Functions, Context, Events, and Multi-Agent Orchestration** are native language primitives.

```text
Program
  ↓
main
  ↓
Agent / MultiAgent / Function / Tool
  ↓
Tasks (with Model Overrides)
  ↓
Tools / Models / Context / Persistent Memory / Messaging
  ↓
Enriched Operation Results & Telemetry
```

---

## 2. Quick Example

```agentlang
// Model alias definition
model fast = mock

// First-class custom tool defined directly in script
tool data_validator {
    description = "Validates structured research findings"
    input text: string
    execute {
        if (text == "") {
            return false
        }
        return true
    }
}

// Zero-trust permission policy
permission ResearchPermission {
    allow browser.search
    allow data_validator
    ask filesystem.write
    cannot terminal
}

// Reusable first-class function
function formatSummary(title, content) {
    return "=== " + title + " ===\n" + content
}

agent Researcher (
    model = fast,
    permission = ResearchPermission,
    tools = [browser, data_validator]
) {
    context topic = "Autonomous AI Agent Architecture"

    task research {
        data = research(topic)
        isValid = data_validator(data.result)
        answer = think("Summarize verified research: " + data.result)
        return answer
    }

    summary = formatSummary(topic, research.result)
    print(summary)
    print("Execution latency: " + research.duration + "ms, Model: " + research.model)
}

main {
    print("Starting AgentLang v0.2.0 workflow...")
    agent Researcher
    print("Execution complete.")
}
```

---

## 3. Core Concepts

### A. First-Class `agent` & Lifecycle
An agent executes top-to-bottom and encapsulates configuration (model, memory, permissions, tools), context state, tasks, and programming logic.

```agentlang
agent Analyst (
    model = GPT,
    permission = AnalystPolicy,
    tools = [browser, calculator]
) {
    ...
}
```

### B. First-Class Functions & Scoping
Declare reusable functions with parameters, return statements, lexical scoping, closures, and call stack recursion guards (max 256 calls):

```agentlang
function calculateScore(factors) {
    total = 0
    for factor in factors {
        total = total + factor
    }
    return total
}
```

### C. Extended Type System & Index Access
AgentLang supports 11 first-class runtime types: `string`, `number`, `boolean`, `list`, `map`, `null`, `agent`, `task`, `operation`, `function`, and `tool`.

```agentlang
items = ["apple", "banana", "cherry"]
print(items[0])                    // Index access: "apple"

config = { tier: "pro", active: true }
print(config["tier"])              // Map access: "pro"

print(type(items))                 // "list"
print(type(config))                // "map"
```

### D. Model System 2.0 (Aliases & Task Overrides)
Define top-level model aliases and override models per-task:

```agentlang
model fast = mock
model heavy = mock

agent MultiModelAgent (model = fast) {
    task quickCheck (model = fast) {
        return think("Quick sanity check")
    }

    task deepAnalysis (model = heavy) {
        return think("Deep multi-step reasoning")
    }
}
```

### E. First-Class Custom Tools
Define custom tools with typed parameters and executable blocks directly within your `.agent` files:

```agentlang
tool calculate_tax {
    description = "Calculates total amount including sales tax"
    input amount: number
    input rate: number
    execute {
        tax = amount * rate
        return amount + tax
    }
}
```

### F. Agent Communication & Messaging
Agents can communicate asynchronously using message passing:

```agentlang
// Send messages with optional tags
send "Quarterly report draft ready" to Reviewer tag "report"

// Inspect agent inbox
for msg in Reviewer.inbox {
    print("From: " + msg.sender + " [Tag: " + msg.tag + "]: " + msg.content)
}
```

### G. Persistent Memory
Agent memory can persist across separate program invocations using `LocalFileMemoryStore` (saved to `.agentlang/memory/{agent}.json`):

```agentlang
agent StatefulAgent (model = mock) {
    task rememberPreference {
        return think("Store user preference: Dark Mode")
    }
}
```

### H. First-Class `task` & Explicit `.result` Access
AgentLang distinguishes between an **Operation** and its **Result**:
- `answer = think(data)` produces an `OperationValue` with rich telemetry (`.status`, `.result`, `.model`, `.duration`, `.error`, `.type`, `.toolCalls`).
- `answer.result` evaluates the actual payload.
- `Analyst.research.result` enables clean cross-agent referencing in MultiAgent workflows.

### I. Zero-Trust `permission` & Human Approval
Security is enforced by the runtime engine before any capability is executed. Model instructions and prompt engineering are **never** treated as security boundaries.

Modes:
- `allow`: Immediate authorized execution.
- `ask`: Runtime pauses and prompts for **Human Approval** (interactive `[y/N]` prompt or `--yes` auto-approve flag).
- `cannot`: Disallowed, throws `AgentLangPermissionException` (`AGT600`).

```agentlang
permission StrictPolicy {
    allow browser.search
    ask filesystem.write
    cannot terminal
}
```

### J. Structured Error Hierarchy (`AGTxxx`)
All AgentLang exceptions adhere to standardized, machine-readable error codes:

| Code | Exception Type | Description |
| :--- | :--- | :--- |
| `AGT100` | `AgentLangSyntaxException` | Parser and syntax errors with line/column pointers |
| `AGT200` | `AgentLangSemanticException` | Semantic errors, unresolved symbols, type mismatches |
| `AGT300` | `AgentLangRuntimeException` | Generic runtime errors |
| `AGT301` | `AgentLangRuntimeException` | Call stack overflow (recursion depth > 256) |
| `AGT302` | `AgentLangRuntimeException` | Index out of range (list, string, or map) |
| `AGT400` | `AgentLangModelException` | LLM provider failures, API errors, or rate limits |
| `AGT500` | `AgentLangToolException` | Tool invocation errors and invalid arguments |
| `AGT600` | `AgentLangPermissionException` | Zero-trust security policy violations |
| `AGT601` | `AgentLangApprovalException` | Human approval denied during interactive confirmation |
| `AGT700` | `AgentLangNetworkException` | Network connection failures |
| `AGT701` | `AgentLangTimeoutException` | Execution and HTTP request timeouts |

---

## 4. Architecture

AgentLang is implemented as a clean, modular multi-project solution in C# 14 / .NET 10:

```text
AgentLang/
├── AgentLang.sln
├── src/
│   ├── AgentLang.AST/          # AST nodes, diagnostics, and structured AGTxxx exceptions
│   ├── AgentLang.Lexer/        # Token scanner, keyword dictionary, line/col tracking
│   ├── AgentLang.Parser/       # Pratt & recursive descent parser with error recovery
│   ├── AgentLang.Semantic/     # Scope resolution, symbol table, Levenshtein suggestions
│   ├── AgentLang.Security/     # Permission policies (allow/ask/cannot) & human approval
│   ├── AgentLang.Models/       # Provider abstractions (Mock, OpenAI, Gemini, Anthropic) & aliases
│   ├── AgentLang.Tools/        # Tool sandbox and in-script custom tool execution
│   ├── AgentLang.Runtime/      # Execution engine, functions, memory stores, messaging & EventBus
│   └── AgentLang.Cli/          # CLI tooling ('agent run', 'repl', 'check', 'format', 'test', etc.)
└── tests/
    ├── AgentLang.Tests/            # Unit test suite (Lexer, Parser, Semantic, Security, Runtime)
    └── AgentLang.IntegrationTests/ # E2E tests executing all 16 runnable examples
```

---

## 5. CLI Usage

The `agent` CLI provides a complete suite of developer tooling:

```bash
# Display version (0.2.0)
agent --version

# Interactive REPL session
agent repl

# Fast static syntax and semantic verification
agent check main.agent
agent check examples/

# Format AgentLang source code (AST pretty-printer)
agent format main.agent
agent format --dry-run examples/

# Run automated tests
agent test tests/

# Execute an AgentLang program
agent run main.agent

# Execute with automated approval (headless / CI mode)
agent run main.agent --yes

# Parse and semantically validate without running
agent build main.agent

# System diagnostics (validates .NET 10, compiler, security sandbox, models, tools)
agent doctor

# Scaffold a new project
agent new MyAgentProject

# Manage package dependencies
agent install browser 1.0.0
agent list
agent remove browser
```

---

## 6. Examples

Explore the complete collection of 16 runnable examples in [`examples/`](examples/):

| Example | Category | Description |
| :--- | :--- | :--- |
| [`hello-agent`](examples/hello-agent/main.agent) | Basics | Minimal entry point with basic agent and output |
| [`functions`](examples/functions/main.agent) | **v0.2.0** | First-class functions, recursion, and type checking |
| [`custom-tools`](examples/custom-tools/main.agent) | **v0.2.0** | In-script custom tool declaration and execution |
| [`agent-communication`](examples/agent-communication/main.agent) | **v0.2.0** | Message passing with `send ... to` and `inbox` |
| [`model-overrides`](examples/model-overrides/main.agent) | **v0.2.0** | Model aliases and task-level model overrides |
| [`persistent-memory`](examples/persistent-memory/main.agent) | **v0.2.0** | Persistent memory retention across agent runs |
| [`structured-errors`](examples/structured-errors/main.agent) | **v0.2.0** | Error handling with typed `AGTxxx` error codes |
| [`simple-researcher`](examples/simple-researcher/main.agent) | Workflows | Research agent with tools, models, and `.result` |
| [`permissions`](examples/permissions/main.agent) | Security | Zero-trust permission policies (`allow`, `ask`, `cannot`) |
| [`tools`](examples/tools/main.agent) | Tools | Built-in tool integration (`browser`, `filesystem`) |
| [`memory`](examples/memory/main.agent) | Memory | Context memory vs. stateless (`memory = null`) agents |
| [`multiagent`](examples/multiagent/main.agent) | Orchestration | Multi-agent collaboration and cross-agent result passing |
| [`parallel`](examples/parallel/main.agent) | Concurrency | Concurrent execution using `parallel { ... }` |
| [`events`](examples/events/main.agent) | Reactive | Event-driven agents reacting to `task.finished` |
| [`retry`](examples/retry/main.agent) | Resilience | Fault-tolerant task retry with exponential backoff |
| [`human-approval`](examples/human-approval/main.agent) | Governance | Guarded execution requiring interactive human approval |

Run any example directly:
```bash
dotnet run --project src/AgentLang.Cli -- run examples/functions/main.agent
```

---

## 7. Model Providers & Zero-Cost Offline Testing

AgentLang includes a deterministic **`MockModelProvider`** enabled by default. **No paid API keys are required to build, test, or run examples.**

To connect real AI models, configure standard environment variables:

| Provider | Environment Variable | Supported Models |
| :--- | :--- | :--- |
| **OpenAI / Compatible** | `OPENAI_API_KEY` | `gpt-4o`, `gpt-4o-mini`, `o1`, `o3-mini`, LocalAI, Ollama |
| **Google Gemini** | `GEMINI_API_KEY` | `gemini-2.5-flash`, `gemini-1.5-pro` |
| **Anthropic** | `ANTHROPIC_API_KEY` | `claude-3-5-sonnet`, `claude-3-haiku` |

---

## 8. Current Status & Roadmap

AgentLang is currently in **v0.2.0**.

### Completed in v0.2.0:
- [x] First-Class Functions (`function name(...) { ... }`), closures, return statements, recursion guard
- [x] Extended Type System (11 types: `string`, `number`, `boolean`, `list`, `map`, `null`, `agent`, `task`, `operation`, `function`, `tool`)
- [x] Index Access (`items[0]`, `map["k"]`, `str[0]`, `agent["inbox"]`) and `type(val)` function
- [x] Model System 2.0 (Model aliases `model fast = mock`, task-level model overrides `task t (model = fast)`)
- [x] First-Class Custom Tools defined directly in `.agent` scripts with parameter validation
- [x] Agent Communication (`send expr to Agent [tag expr]` and `Agent.inbox`)
- [x] Persistent Memory Store (`IPersistentMemoryStore`, `LocalFileMemoryStore`, `InMemoryMemoryStore`)
- [x] Rich Operation Telemetry (`status`, `result`, `model`, `duration`, `error`, `type`, `toolCalls`)
- [x] Structured Error Hierarchy with machine-readable `AGTxxx` codes
- [x] New CLI Developer Commands (`agent repl`, `agent check`, `agent format`, `agent test`)
- [x] 16 Executable Examples and 53 automated unit/integration tests with 100% pass rate
- [x] 100% Backward Compatibility with v0.1.0-alpha

### Roadmap for v0.3.0:
- [ ] Native streaming responses for interactive CLI sessions
- [ ] Language Server Protocol (LSP) for VS Code and Antigravity IDE
- [ ] Persistent vector memory integration (embeddings & semantic search)
- [ ] Distributed Agent Runtime over WebSockets / gRPC

---

## 9. License

AgentLang is released under the [MIT License](LICENSE).
