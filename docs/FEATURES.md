# AgentLang Language & Runtime Reference (`v0.9.2`)

> This document contains the full reference for AgentLang's language primitives, VM subsystems, CLI tools, and experimental modules. For the quickstart, benchmarks, and stability matrix, see the [main README](../README.md).

---

## Table of Contents

1. [Core Language & Scripting DSL](#1-core-language--scripting-dsl)
2. [Compiler, Semantic Analysis & Bytecode VM (`.agtc`)](#2-compiler-semantic-analysis--bytecode-vm-agtc)
3. [Model Providers, Streaming & Offline Execution](#3-model-providers-streaming--offline-execution)
4. [Tooling, MCP Protocol & IDE Support (LSP 3.17)](#4-tooling-mcp-protocol--ide-support-lsp-317)
5. [Experimental & Early-Stage Subsystems](#5-experimental--early-stage-subsystems)
6. [Full CLI Command Reference](#6-full-cli-command-reference)

---

## 1. Core Language & Scripting DSL

### Agent Scripting & Top-Level Execution
Define agents declaratively with model configurations, personas, strategies, tools, and memory stores:

```agentlang
agent ResearchAssistant {
    model: "claude-3-7-sonnet"
    persona: "Rigorous research analyst focusing on empirical evidence"
    tools: [web_search]
    memory: ConversationBuffer(window: 5)
    strategy: ReAct(max_steps: 4)
}

goal task1 = "Summarize latest developments in Autonomous Agent VMs."
response = ResearchAssistant.achieve(task1)
print(response.output)
```

### Multi-Agent Collaborative Pipelines (`pipeline`)
```agentlang
agent Writer {
    model: "gpt-4o"
    role: "Drafts comprehensive technical reports"
}

agent Critic {
    model: "claude-3-7-sonnet"
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
```

### Shared State (`state`) & Team Workflows (`workflow`)
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

### First-Class Schemas (`schema`) & Modular Imports
```agentlang
import "./helpers.agt";
import { sha256 } from "@stdlib/crypto";
import { trim, to_upper } from "@stdlib/text";

schema SecurityAuditReport {
    target: string,
    vulnerabilities_found: int(min: 0, max: 1000),
    risk_level: string(regex: "^(low|medium|high|critical)$"),
    passed: bool,
    cve_tags: [string]
}
```
- Circular import detection (`MOD002`) and `./agent_modules/` package resolution via `agent install` and `agent.lock`.

---

## 2. Compiler, Semantic Analysis & Bytecode VM (`.agtc`)

### Static Diagnostics & AI-Readable JSON (`agent check --json`)
Run compiler checks before touching any LLM endpoint:
```bash
agent check examples/ --json
```
Outputs deterministic error codes (`[AL1002]`, `[AL2006]`, `[AL-AUTH-001]`, etc.), line/column spans, snippets, and repair suggestions.

### Bytecode Intermediate Representation (`.agtc`)
```bash
agent compile examples/hello-agent/main.agent -o hello.agtc
agent run hello.agtc
```

### Sandboxed Virtual Machine (`AgentLang.VM`)
- **Instruction Cycle Limit**: Prevents runaway infinite loops (`VmCycleLimitExceededException` / `VM101`).
- **Capability Gating**: Zero-trust permission checks on sensitive tools (`VmSandboxViolationException` / `VM102`).
- **State Checkpointing**: `vm.CreateSnapshot()` / `vm.RestoreSnapshot()` and persistent `.agtchk` checkpoints under `.agent/checkpoints/`.

---

## 3. Model Providers, Streaming & Offline Execution

- **Live Token Streaming**: Token-by-token streaming in interactive CLI (`agent chat`), script runner (`agent run --stream`), and HTTP Server-Sent Events (`POST /api/v1/stream`).
- **Fail-Fast Authentication**: Unconfigured API providers immediately return actionable auth error codes (`[AL-AUTH-001]`–`[AL-AUTH-004]`) rather than silent mock strings. Explicit offline testing uses `model: "mock"` or `AGENTLANG_OFFLINE_MOCK=1`.
- **Supported Providers**: OpenAI, Google Gemini, Anthropic Claude, DeepSeek, and local **Ollama** (`/api/generate` + NDJSON streaming).
- **Configurable Aliases**: Common short aliases resolve automatically (e.g., `gpt` -> `gpt-4o`, `gemini` -> `gemini-2.5-pro`, `claude` -> `claude-3-7-sonnet`), alongside full model string pass-through.

---

## 4. Tooling, MCP Protocol & IDE Support (LSP 3.17)

- **Model Context Protocol (MCP)**: JSON-RPC 2.0 `McpClient` and stdio `McpServer` (`agent serve --mcp`).
- **HTTP Gateway & Web Playground**: `agent serve --http 8080` exposes `/api/v1/run`, `/api/v1/stream` (SSE), `/api/v1/resume`, `/api/v1/status`, and an interactive web playground at `/`.
- **Language Server Protocol (`agent lsp`)**: Real-time diagnostics, completions, hover docs, document symbols, go-to-definition, formatting, code actions, rename, and references, paired with `editors/vscode/`.
- **Hot-Reload Watcher**: `agent watch main.agt --run`.

---

## 5. Experimental & Early-Stage Subsystems

> **Transparency Note:** The subsystems below exist in the C# codebase and pass unit tests, but are currently early-stage/foundational prototypes compared to the core compiler, VM, streaming, and LSP engines. APIs may change before `v1.0.0`:

- **Actor Model Concurrency (`AgentActorSystem`)**: In-process async mailbox message passing and supervision strategies.
- **StateGraph & Durable Suspension (`AgentLang.VM.Durable`)**: Cyclic workflow state machines with HITL suspension signals.
- **Hierarchical Planning (`PlanAndExecuteEngine`, `ReWooEngine`, `TreeOfThoughtsEngine`)**: Multi-step DAG planners in `AgentLang.VM.Planning`.
- **Security DLP & Resource Boundaries (`AgentLang.Security`)**: Pattern-based secret redaction (`--dlp`) and path/domain policy guards (353 LOC foundational module).
- **Graph Memory & Hybrid Vector Store (`EntityGraphMemory`, `HybridVectorStore`)**: In-memory entity relation graph and BM25 + cosine RRF retrieval.
- **Code Transpilation (`--target python|ts`) & VCR Cassettes (`CassetteRecorder`)**: Starter code emitters and deterministic tool replay fixtures.
- **OpenTelemetry Execution Tracing (`--trace`)**: Local `.agent/traces/{traceId}.json` JSON span tree generation.

---

## 6. Full CLI Command Reference

| Command | Description |
| :--- | :--- |
| `agent run <file> [--trace] [--stream] [--dlp]` | Execute a script (`.agt`, `.agent`, or `.agtc`) |
| `agent check [target] [--json]` | Run syntax and semantic analyzer diagnostics |
| `agent compile <file> [-o <out>]` | Compile source script into `.agtc` bytecode |
| `agent chat <file> [agent]` | Interactive terminal chat with live streaming |
| `agent repl` | Launch interactive REPL shell |
| `agent watch <file> [--run]` | Watch source file and re-check/run on save |
| `agent serve [--mcp \| --http [port] \| --lsp]` | Start MCP server, HTTP/SSE gateway, or LSP |
| `agent lsp` | Start standard Language Server Protocol over stdio |
| `agent init [name]` / `agent new <name>` | Scaffold `agent.config.json` or a full project |
| `agent install <pkg> [ver]` | Sync dependencies from `agent.config.json` into `./agent_modules/` & `agent.lock` |
| `agent remove <pkg>` / `update <pkg>` / `list` | Manage project dependencies |
| `agent build <file> [--target python\|ts]` | Validate or transpile to Python / TypeScript |
| `agent eval <file> --dataset <json>` | Evaluate agent outputs against a benchmark dataset |
| `agent plan "<goal>" [--tools] [--execute]` | Generate or execute a task DAG |
| `agent checkpoint [list\|show]` | Inspect saved execution checkpoints |
| `agent pack [dir]` / `agent docker [dir]` | Bundle `.agtpkg` archive or generate `Dockerfile` |
| `agent format [target] [-w]` | Format AgentLang source files |
| `agent doctor` | Inspect runtime, compiler, tools, and configured API keys |
