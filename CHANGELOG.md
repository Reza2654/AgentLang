# Changelog

All notable changes to **AgentLang** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.6.5] — 2026-10-23

### Added
- **Native Model Context Protocol (MCP) Client & Server (`AgentLang.VM.MCP`)**:
  - `McpClient`: Connect to external MCP tool servers via JSON-RPC 2.0 (stdio streams and in-memory test transports).
  - Protocol handshake (`initialize`, `notifications/initialized`, protocol version `2024-11-05`).
  - Tool discovery (`tools/list`), tool invocation (`tools/call`), resource listing and reading (`resources/list`, `resources/read`).
  - `McpToolBridge`: Seamlessly bridges external MCP tools directly into AgentLang's native `ToolRegistry`.
  - `McpServer`: Expose AgentLang agents and tools as a standard MCP server over stdio (`agent serve --mcp`), allowing Claude Desktop, Antigravity, or Cursor to use AgentLang agents directly as MCP tools.
- **Resumable State Checkpointing & Time-Travel Store (`AgentLang.VM.Checkpointing`)**:
  - `ExecutionCheckpoint`: Complete serializable snapshot of runtime variables, step indexes, agent conversation memories, and execution status (`Active`, `PausedWaitingApproval`, `Completed`, `Failed`).
  - `ICheckpointStore`: Storage backends including `MemoryCheckpointStore` and `FileCheckpointStore` persisting `.agtchk` JSON files under `.agent/checkpoints/`.
  - `CheckpointManager`: Checkpoint creation, listing, and state restoration into virtual machine instances (`RestoreCheckpointAsync`).
  - CLI command: `agent checkpoint [list | show <id>]`.
- **Multi-Agent Swarm Protocols & Consensus Engines (`AgentLang.VM.Swarm`)**:
  - `SwarmHandoffRouter`: Contextual task delegation between agents with return-on-completion synthesis (`ReturnOnComplete`).
  - `AgentConsensusEngine`: Multi-agent voting tally with `VotingStrategy` (`Majority`, `Weighted`, `Unanimous`) and threshold validation.
  - `DebateEngine`: Multi-round structured debates between opposing agents with moderator/judge final verdict synthesis.
- **Distributed Tracing & OpenTelemetry Observability (`AgentLang.VM.Observability`)**:
  - `TraceContext`: W3C TraceContext compliant (`00-{traceId}-{spanId}-{flags}`) with automatic parent-child propagation via `AsyncLocal`.
  - `AgentSpan`: Records span lifecycle, duration, prompt/completion token usage, and cost in USD.
  - Exporters: `ConsoleTraceExporter` (colored ASCII execution tree) and `JsonTraceExporter` (OpenTelemetry-compliant JSON files under `.agent/traces/`).
  - CLI flag: `agent run <file.agt> --trace` to visualize real-time span execution trees.
- **Testing**:
  - Added `V065Tests.cs` (8 comprehensive tests covering MCP client/server, checkpointing, swarm handoffs, consensus voting, debate protocol, and distributed tracing). Total: 133 unit tests + 18 integration tests = 151 tests (100% pass rate).

---

## [0.6.4] — 2026-10-22

### Added
- **Actor Model & Concurrency Engine (`AgentLang.VM.Actors`)**:
  - `AgentActor`: Isolated actor instances running asynchronous processing loops with dedicated `ActorMailbox` (lock-free unbounded channels).
  - `AgentActorSystem`: Actor lifecycle management, address lookup, async message passing (`AskAsync`, `TellAsync`, `SendAsync`), and broad broadcast dispatch (`BroadcastAsync`).
  - **Supervision & Fault Tolerance**: Configurable `SupervisionStrategy` (`Resume`, `Restart`, `Stop`) with automatic fault containment and restart counters.
- **Context Compaction & Token Caching (`AgentLang.VM.Optimization`)**:
  - `ContextCompactor`: Automated context compaction (`CompactHistory`) consolidating older conversation turns into unified system memos while preserving recent turns.
  - `EstimateTokens` and `PruneContext` for context window protection.
  - `TokenCache`: High-performance deterministic prompt and tool execution memory cache with TTL, hit/miss metrics, and hit-rate tracking.
- **Tool Scheduler & Rate Limiter (`AgentLang.VM.Scheduling`)**:
  - `RateLimiter`: Sliding-window request limiter preventing provider throttling and HTTP 429 errors.
  - `ToolScheduler`: Managed concurrency semaphore and rate-limited queue for smooth tool and model execution.
- **Micro-Agent Containerization**:
  - `ProjectManager.GenerateDockerFiles`: Automated generation of production-ready multi-stage `Dockerfile` and `.dockerignore`.
  - CLI command `agent docker [dir] [entry]` and `agent build --docker` flag.
- **Testing**:
  - Added `V064Tests.cs` covering actor communication, supervision restarts, context compaction, token caching, tool scheduling, and Docker generation (125/125 total passing tests).

---

## [0.6.3] — 2026-10-21

### Added
- **Agent Virtual Machine & Sandbox Execution Engine (`AgentLang.VM`)**:
  - Memory and execution isolation via `AgentVirtualMachine`.
  - Instruction cycle limit enforcement with `VmCycleLimitExceededException` (`VM101`).
  - Capability gating and sandboxing security violations via `VmSandboxViolationException` (`VM102`).
  - State snapshotting and restoration with `VmSnapshot` and JSON serialization (`ToJson` / `FromJson`).
- **Multi-File Modular Imports**:
  - Full support for `import "./module.agt"` and `import { Sym1, Sym2 } from "./module.agt"`.
  - Circular import detection and prevention (`MOD002`).
  - In-memory module scope caching for optimal performance.
- **Standard Library Packages (`@stdlib/*`)**:
  - `@stdlib/crypto`: `sha256`, `md5`, `uuid`, `random_token`.
  - `@stdlib/text`: `split`, `join`, `trim`, `matches`, `replace`.
  - `@stdlib/rag`: `chunk_text`, `similarity`.
- **Package Management & Bundling**:
  - Project manifest `agent.json` and lockfile `agent.lock` with SHA-256 integrity hashes.
  - CLI command `agent pack` bundling projects into distributable `.agtpkg` archives.

---

## [0.6.2] — 2026-10-21

### Added
- **Code Transpilers**:
  - Python Transpiler (`--target python`): Emits idiomatic Python code compatible with LangGraph and CrewAI.
  - TypeScript Transpiler (`--target ts`): Emits TypeScript code compatible with Vercel AI SDK.
- **Vector Store Memory**:
  - `VectorStoreMemory`: In-memory semantic vector store with cosine similarity retrieval, multi-lingual tokenization, and metadata filtering.
- **Reactive Event Engine**:
  - First-class event-driven agents via `on event(Payload) { ... }`.
  - Semantic decision blocks (`decide (log.content contains_suspicious_activity)`).
  - Safety actions: `block_ip`, `send_slack_alert`, `emit_reply`.
- **Dynamic Heuristic Generator**:
  - Replaced hardcoded response patterns with semantic intent analysis combining role, persona, and query context.

---

## [0.6.1] — 2026-10-21

### Added
- **Guardrails Engine**:
  - `guardrails { forbid: [...], require_format: Schema(...) }` block syntax.
  - Runtime enforcement with `GuardrailViolationException` (`AGT601`).
- **Token & Cost Budgeting**:
  - `budget max_tokens: N max_cost: C { ... }` block scoping.
  - Cumulative tracking and pre-call budget checks throwing `BudgetExceededException` (`AGT602`).
- **Human-in-the-Loop (HITL) Approvals**:
  - `transfer_to_human(reason, customer_id)` runtime escalation.
  - Non-blocking approval provider architecture.

---

## [0.6.0] — 2026-10-21

### Added
- **Agent Scripting DSL Overhaul**:
  - Modern official file extension `.agt` with full backward compatibility for `.agent`.
  - Top-level scripting (executing statements directly without requiring `main { }`).
  - Declarative agent syntax with `model`, `persona`, `strategy`, `tools`, `memory`.
  - First-class goals (`goal task1 = "..."`) and goal-oriented execution (`agent.achieve(goal)`).
  - Multi-agent collaborative pipelines (`pipeline`) with evaluation retry loops (`loop until ... max_retries`).
  - Shared state structures (`state`) and team workflows (`workflow`).
  - Semantic decision-making (`decide`) with reasoning blocks and case statements.

---

## [0.5.0] — 2026-10-02

### Added
- **Autonomous ReAct Reasoning Engine**:
  - Autonomous thought-action-observation cycle (`agent.solve(prompt)`).
- **Conversational Bot System**:
  - Stateful multi-turn conversations (`agent.chat(message)`).
  - Conversation history, session management, and reset capabilities.
- **Interactive Terminal Chat**:
  - CLI command `agent chat <file> [AgentName]` with colored thought visualization.

---

## [0.4.0] — 2026-09-27

### Added
- **Multi-Agent Swarm Primitives**:
  - Hierarchical and mesh swarms (`swarm ResearchSwarm { ... }`).
  - Agent delegation (`delegate task to Agent`).
  - Inter-agent messaging and broadcast channels.
- **MCP (Model Context Protocol) Support**:
  - Native client support for external MCP tool servers.

---

## [0.3.1] — 2026-09-26

### Added
- Model registry enhancements and offline mock model heuristic simulation.
- CLI code formatting (`agent format`) and static syntax checking (`agent check`).

---

## [0.3.0] — 2026-09-26

### Added
- Custom tool definitions directly in AgentLang scripts (`tool name { ... }`).
- Persistent file-based memory store (`LocalFileMemoryStore`).
- Zero-trust capability permissions (`permission { allow, ask, cannot }`).

---

## [0.2.1] — 2026-09-25

### Added
- Search API integration (`Tavily`, `Serper`, `Brave`, generic).
- Strict runtime validation requiring API keys for live search queries.
- Automatic `.env` configuration file loader.

---

## [0.2.0] — 2026-09-25

### Added
- First-class functions, scoped variables, recursion depth limits (`AGT301`).
- Extended type system with 11 runtime types.
- REPL interactive shell (`agent repl`).
- System diagnostics (`agent doctor`).

---

## [0.1.0-alpha] — 2026-09-25

### Added
- Initial language architecture, lexer, parser, AST, and tree-walk interpreter.
- Basic agent declarations, tasks, and tool bindings.
