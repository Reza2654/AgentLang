# AgentLang — v0.1.0-alpha

> **An Agent-Native Programming Language for Autonomous AI Systems**  
> *Built with modern C# on .NET 10.*

[![Build and Test](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%2014-blue)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Version](https://img.shields.io/badge/Release-v0.1.0--alpha-orange)](https://github.com/Reza2654/AgentLang/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

---

## 1. What is AgentLang?

**AgentLang** is a programming language designed from the ground up to make **AI Agents** first-class programming constructs.

Instead of writing complex glue code across multiple Python libraries, prompt templates, and brittle API wrappers, AgentLang provides a cohesive, deterministic, and secure syntax where **Agents, Tasks, Models, Tools, Permissions, Context, Events, and Multi-Agent Orchestration** are native language primitives.

```text
Program
  ↓
main
  ↓
Agent / MultiAgent
  ↓
Tasks
  ↓
Tools / Models / Context / Memory
  ↓
Results
```

---

## 2. Quick Example

```agentlang
permission ResearchPermission {
    allow browser.search
    ask filesystem.write
    cannot terminal
}

agent Researcher (
    model = GPT,
    permission = ResearchPermission,
    tools = [browser, filesystem]
) {
    context topic = "Autonomous AI Agent Architecture"

    task research {
        data = research(topic)
        answer = think(data.result)
        return answer
    }

    print("Research Output: " + research.result)
}

main {
    print("Starting AgentLang workflow...")
    agent Researcher
    print("Execution complete.")
}
```

---

## 3. Core Concepts

### A. First-Class `agent`
An agent executes top-to-bottom and encapsulates configuration (model, memory, permissions, tools), context state, tasks, and programming logic.

```agentlang
agent Researcher (
    model = GPT,
    permission = ResearchPermission
) {
    ...
}
```

### B. First-Class `task` & Explicit `.result` Access
A `task` represents a discrete unit of work inside an agent.
Crucially, AgentLang distinguishes between an **Operation** and its **Result**:
- `answer = think(data)` produces an `OperationValue` (containing execution latency, model used, tool calls, and status).
- `answer.result` evaluates the actual output string or object payload.
- `research.result` accesses the returned result of the `research` task.
- `Researcher.research.result` enables clean cross-agent referencing in MultiAgent workflows.

### C. Zero-Trust `permission` & Human Approval
Security is enforced by the runtime engine before any capability is executed. Model instructions and prompt engineering are **never** treated as security boundaries.

Modes:
- `allow`: Immediate authorized execution.
- `ask`: Runtime pauses and prompts for **Human Approval** (interactive `[y/N]` prompt or `--yes` auto-approve flag).
- `cannot`: Disallowed, throws a `SecurityException`.

```agentlang
permission StrictPolicy {
    allow browser.search
    ask filesystem.write
    cannot terminal
}
```

### D. MultiAgent & Parallel Execution
Orchestrate collaborative agent systems with sequential execution or concurrent `parallel { ... }` blocks:

```agentlang
multiagent ResearchTeam {
    parallel {
        agent TrendsAnalyst (model = GPT) {
            task analyze { return think("Analyze technology trends") }
        }
        agent RiskAnalyst (model = Claude) {
            task analyze { return think("Analyze compliance risks") }
        }
    }

    agent Synthesizer (model = Gemini) {
        task merge {
            combined = TrendsAnalyst.analyze.result + "\n" + RiskAnalyst.analyze.result
            return think("Synthesize: " + combined)
        }
    }
}
```

### E. Events
Trigger reactive workflows when tasks complete:

```agentlang
event research.finished {
    agent Reviewer
}
```

### F. Resilient Error Handling & Retry
```agentlang
try {
    retry 3 {
        data = research(topic)
    }
} catch err {
    print("Failed after 3 retries: " + err)
}
```

---

## 4. Architecture

AgentLang is implemented as a clean, modular multi-project solution in C# / .NET 10:

```text
AgentLang/
├── AgentLang.sln
├── src/
│   ├── AgentLang.AST/          # Abstract syntax tree nodes, source spans, diagnostics
│   ├── AgentLang.Lexer/        # Token scanner, keyword dictionary, line/col tracking
│   ├── AgentLang.Parser/       # Pratt & recursive descent parser with error recovery
│   ├── AgentLang.Semantic/     # Scope resolution, symbol table, Levenshtein suggestions
│   ├── AgentLang.Security/     # Permission policies (allow/ask/cannot) & human approval
│   ├── AgentLang.Models/       # Provider abstractions (Mock, OpenAI, Gemini, Anthropic)
│   ├── AgentLang.Tools/        # Tool sandbox (filesystem, browser, terminal, http, calculator)
│   ├── AgentLang.Runtime/      # Execution engine, Agent/Task lifecycle, Memory & EventBus
│   └── AgentLang.Cli/          # CLI tooling ('agent run', 'build', 'new', 'doctor', etc.)
└── tests/
    ├── AgentLang.Tests/            # Unit test suite (Lexer, Parser, Semantic, Security, Runtime)
    └── AgentLang.IntegrationTests/ # E2E tests executing all 10 runnable examples
```

---

## 5. CLI Usage

The `agent` CLI provides complete developer tooling:

```bash
# Display version
agent --version

# System diagnostics (validates .NET 10, compiler, security sandbox, models, tools)
agent doctor

# Scaffold a new project
agent new MyAgentProject

# Execute an AgentLang program
agent run main.agent

# Execute with automated approval (headless / CI mode)
agent run main.agent --yes

# Parse and semantically validate without running
agent build main.agent

# Manage package dependencies
agent install browser 1.0.0
agent list
agent remove browser
```

---

## 6. Examples

Explore the growing collection of runnable examples in [`examples/`](examples/):

| Example | Description |
| :--- | :--- |
| [`hello-agent`](examples/hello-agent/main.agent) | Minimal entry point with basic agent and output |
| [`simple-researcher`](examples/simple-researcher/main.agent) | Research agent with tools, models, and `.result` |
| [`permissions`](examples/permissions/main.agent) | Zero-trust permission policies (`allow`, `ask`, `cannot`) |
| [`tools`](examples/tools/main.agent) | Tool registry integration (`browser`, `filesystem`) |
| [`memory`](examples/memory/main.agent) | Context memory vs. stateless (`memory = null`) agents |
| [`multiagent`](examples/multiagent/main.agent) | Multi-agent collaboration and cross-agent result passing |
| [`parallel`](examples/parallel/main.agent) | Concurrent execution using `parallel { ... }` |
| [`events`](examples/events/main.agent) | Event-driven agents reacting to `task.finished` |
| [`retry`](examples/retry/main.agent) | Fault-tolerant task retry with exponential backoff |
| [`human-approval`](examples/human-approval/main.agent) | Guarded execution requiring interactive human approval |

Run any example directly:
```bash
dotnet run --project src/AgentLang.Cli -- run examples/parallel/main.agent
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

AgentLang is currently in **v0.1.0-alpha**.

### Completed in v0.1.0-alpha:
- [x] Full Lexer, Parser, and AST with rich source-snippet diagnostics
- [x] Semantic Analyzer with Levenshtein symbol suggestions
- [x] Zero-Trust Permission Engine (`allow`, `ask`, `cannot`) + Human Approval
- [x] Model Provider Abstraction (Mock, OpenAI, Gemini, Anthropic)
- [x] Sandboxed Tool Registry (`filesystem`, `browser`, `terminal`, `http`, `calculator`)
- [x] Agent & Task Execution Engine with distinct `operation` vs. `.result` access
- [x] Memory & Context System (`context var = val`, `memory = null`)
- [x] MultiAgent Orchestration & Concurrency (`parallel { ... }`)
- [x] Reactive Event Bus (`event <task>.finished`)
- [x] Error Handling (`try / catch`) & Resilient Retries (`retry N`)
- [x] Comprehensive CLI (`new`, `run`, `build`, `doctor`, `install`, `remove`, `update`, `list`)
- [x] 10 Executable Examples & Automated Integration Test Suite

### Roadmap for v0.2.0:
- [ ] Persistent vector memory integration (embeddings & semantic search)
- [ ] Native streaming responses for interactive CLI sessions
- [ ] Language Server Protocol (LSP) for VS Code and Antigravity IDE
- [ ] Declarative custom tool authoring syntax in `.agent` scripts

---

## 9. License

AgentLang is released under the [MIT License](LICENSE).
