# AgentLang — v0.9.0

> **A Statically-Checked DSL, Compiler, and Stack-Based Bytecode VM for Autonomous AI Agents**  
> *Built from scratch in C# 14 on .NET 10 with zero external runtime dependencies (Standard BCL only).*

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23%2014-blue)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Release](https://img.shields.io/badge/Release-v0.9.0-success)](https://github.com/Reza2654/AgentLang/releases)
[![Tests](https://img.shields.io/badge/Tests-241%20Passing%20(100%25)-brightgreen)](https://github.com/Reza2654/AgentLang)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

> **Note on Naming:** This repository (`Reza2654/AgentLang`) is an independent, MIT-licensed compiler and bytecode VM implemented in C#/.NET. It is unrelated to Fractl's TypeScript/Node.js web application generator (`agentlang-ai/agentlang`).

---

## ⚡ Why AgentLang?

Building multi-agent pipelines in Python (LangChain, CrewAI, AutoGen) typically entails large dependency trees, runtime type mismatches, and opaque tracebacks deep inside dynamic glue code.

AgentLang treats agent orchestration as a **compiled systems language**:

| Dimension | 🚀 AgentLang (`Reza2654/AgentLang`) | 🐍 Python Frameworks (LangChain / CrewAI) |
| :--- | :--- | :--- |
| **Execution Model** | Custom Lexer → Parser → AST → Semantic Analyzer → Bytecode VM (`.agtc`) | Python runtime libraries & dynamic wrappers |
| **Static Verification** | Catches syntax, type, and tool errors **before** any LLM API call (`agent check --json`) | Fails at runtime after partial API spend |
| **External Dependencies** | **0** runtime NuGet dependencies (100% .NET Standard BCL) | 30–100+ transitive pip packages |
| **Distribution Size** | **12.36 MB** self-contained single-file executable (verified, zero .NET install needed) | 400 MB+ Python virtual environments |
| **Diagnostics for Humans & AI** | Structured `--json` compiler errors with error codes (`[AL-AUTH-001]`), spans & fix suggestions | Multi-page dynamic stack traces |
| **Live Token Streaming** | Built-in token-by-token streaming in CLI (`agent chat`) and HTTP SSE gateway | Custom callback/iterator wiring |
| **IDE Tooling** | Native **LSP 3.17** server (`agent lsp`) + VS Code extension (`editors/vscode`) | Generic Python linters |
| **Local / Air-Gapped LLMs** | Native **Ollama** streaming provider + zero-trust tool sandboxing | Community extensions / config |

---

## 🏛️ 4 Core Architectural Pillars

Instead of wrapping existing agent libraries, AgentLang is built around four verified core subsystems:

### 1. Agent-Native DSL (`.agt` / `.agent`) & Semantic Verification
Write typed `agent`, `pipeline`, `workflow`, `state`, `schema`, and `tool` declarations that are statically checked prior to execution:

```agentlang
agent ResearchAssistant {
    model: "gemini-2.5-pro"
    role: "Empirical technical analyst"
    tools: [web_search]
}

goal task = "Summarize consensus models in decentralized agent systems."
response = ResearchAssistant.achieve(task)
print(response.output)
```

Run machine-readable compiler checks for CI pipelines or AI coding assistants:
```bash
agent check examples/ --json
```

### 2. Stack-Based Bytecode VM (`.agtc`) & Sandboxing
Compile `.agt` / `.agent` scripts down to compact `.agtc` bytecode binaries (e.g., `160 bytes` for `examples/hello-agent/main.agent`) and execute inside a sandboxed VM supporting instruction cycle limits (`VM101`), capability gating (`VM102`), and execution snapshots:

```bash
agent compile examples/hello-agent/main.agent -o hello.agtc
agent run hello.agtc
```

### 3. Multi-Provider Connectivity, Live Token Streaming & MCP
* **Zero-Mock Fail-Fast Auth:** Missing API credentials fail immediately with actionable codes (`[AL-AUTH-001]`–`[AL-AUTH-004]`) instead of returning silent placeholder strings. Deterministic offline mocks are scoped explicitly to `model: "mock"` or test hosts.
* **Live Token Streaming:** Word-by-word streaming over OpenAI, Gemini, Claude, DeepSeek, and local **Ollama**, exposed in both interactive CLI (`agent chat`) and HTTP Server-Sent Events (`POST /api/v1/stream`).
* **Model Context Protocol (MCP):** JSON-RPC 2.0 `McpClient` and stdio `McpServer` (`agent serve --mcp`).

### 4. Language Server Protocol (LSP 3.17) & Project Workflow
* Full **LSP 3.17** implementation (`agent lsp`): real-time diagnostics, completions, hover documentation, go-to-definition, document outline, formatting, rename, and references (`editors/vscode`).
* Modular file imports (`import "./helpers.agt";`) and project management (`agent init`, `agent install`, `agent.config.json`, `agent.lock`).

> 📖 For the complete language syntax, CLI flags, HTTP gateway API, and full module guide, see **[docs/FEATURES.md](docs/FEATURES.md)**.

---

## ⚡ 60-Second Quickstart

### Build & Run from Source (`.NET 10 SDK`)
```bash
git clone https://github.com/Reza2654/AgentLang.git
cd AgentLang
dotnet build AgentLang.sln
```

Run static verification across all 24 included examples:
```bash
dotnet run --project src/AgentLang.Cli -- check examples/
```

Compile an example to `.agtc` bytecode:
```bash
dotnet run --project src/AgentLang.Cli -- compile examples/hello-agent/main.agent -o hello.agtc
```

Run an interactive live-streaming chat (set `GEMINI_API_KEY`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, or use local `ollama`):
```bash
dotnet run --project src/AgentLang.Cli -- chat examples/hello-agent/main.agent
```

Check environment health and configured providers:
```bash
dotnet run --project src/AgentLang.Cli -- doctor
```

---

## 📊 Reproducible Benchmarks

All metrics below were measured locally using [`benchmarks/benchmark.ps1`](benchmarks/benchmark.ps1) on `win-x64` (`.NET 10.0.12`, `v0.9.0`, averaged across 5 runs after warm-up):

| Publish Configuration | Binary Size | `--version` (avg) | `check examples/ --json` (24 files) | `hello.agtc` Size |
| :--- | :---: | :---: | :---: | :---: |
| Self-Contained + Trimmed + Compressed (`PublishSingleFile`) | **12.36 MB** | `135.2 ms` | `281.9 ms` | `160 bytes` |
| Self-Contained + ReadyToRun (Uncompressed `PublishSingleFile`) | **83.31 MB** | `61.8 ms` | `107.3 ms` | `160 bytes` |

Reproduce these measurements directly on your machine:
```powershell
powershell -ExecutionPolicy Bypass -File benchmarks/benchmark.ps1
```

> **Note on Full Native AOT (`-p:PublishAot=true`):** Core compiler/CLI JSON diagnostics (`CheckCommand`, `Diagnostics`) use zero-reflection `Utf8JsonWriter`, while optional runtime/VM serialization components (REST providers, MCP server, HTTP gateway) still use `System.Text.Json` reflection serialization (emitting trim/AOT warnings `IL2026`/`IL3050` during publish). Full zero-warning Native AOT across all VM modules is tracked on the roadmap toward `v1.0.0`.

---

## 🔍 Subsystem Maturity Transparency

We separate battle-tested core features from foundational modules so evaluators know exactly what is ready for daily use today:

| Subsystem | Status | Codebase Footprint |
| :--- | :---: | :--- |
| Lexer, Parser, AST & Semantic Analyzer | 🟢 **Core Stable** | 37 files · 5,569 LOC · tested across 241 cases |
| CLI, `check --json`, HTTP/SSE Gateway & LSP 3.17 | 🟢 **Core Stable** | 2,340+ LOC CLI + VM server/LSP subsystems |
| Model Providers (OpenAI, Gemini, Claude, DeepSeek, Ollama) & Streaming | 🟢 **Core Stable** | 13 files · 1,603 LOC · fail-fast auth + SSE/NDJSON |
| Bytecode Compiler & Sandboxed Stack VM (`.agtc`) | 🟢 **Core Stable** | Part of `AgentLang.VM` (6,967 LOC total) |
| Hierarchical Planners (ReWOO / ToT), Entity Graph Memory, DLP Security, Transpilers | 🟡 **Foundational / Experimental** | Unit-tested prototypes; APIs evolving toward `v1.0.0` |

---

## 🧪 Testing & Verification

The solution includes **241 automated tests (223 unit tests + 18 integration tests)** across compiler, semantic analysis, bytecode VM, MCP, streaming, and example scripts:

```bash
dotnet test AgentLang.sln
```

```text
Passed!  - Failed: 0, Passed: 223, Skipped: 0, Total: 223 - AgentLang.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  18, Skipped: 0, Total:  18 - AgentLang.IntegrationTests.dll (net10.0)
```

---

## 📄 License

AgentLang is licensed under the [MIT License](LICENSE).
