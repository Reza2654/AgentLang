# Changelog

All notable changes to **AgentLang** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [0.8.0] — 2026-10-26

### Added
- **Language Server Protocol (LSP) Server (`AgentLang.VM.Lsp`)**:
  - `AgentLspServer`: Standard JSON-RPC 2.0 language server running over `stdio` with standard `Content-Length` framing (`LspTransport`).
  - Real-Time Diagnostics (`textDocument/publishDiagnostics`): Live background parsing and semantic analysis emitting error markers with exact 0-based LSP ranges and diagnostic codes (`AL0038`, `AL2003`, etc.).
  - IntelliSense Auto-Completion (`textDocument/completion`): Context-aware suggestions for keywords (`agent`, `schema`, `workflow`, `pipeline`, `state`, `goal`, `achieve`, `decide`), built-in models (`gpt-4o`, `claude-3-5-sonnet`, `llama3.2`, `mistral:7b`), tools, and declared symbols.
  - Interactive Hover Documentation (`textDocument/hover`): Markdown docstrings and signature details for language primitives and user-defined agents/schemas.
  - Document Outline & Breadcrumbs (`textDocument/documentSymbol`): Hierarchical outline of agents, schemas, fields, pipelines, and workflows.
  - CLI commands: `agent lsp` and `agent serve --lsp`.
- **Official VS Code Extension (`editors/vscode`)**:
  - Language definition for `.agt` and `.agent` files with TextMate grammar syntax highlighting (`syntaxes/agentlang.tmLanguage.json`).
  - Bracket matching, auto-closing pairs, and comment formatting (`language-configuration.json`).
  - Extension manifest (`package.json`) and developer documentation.
- **Testing**:
  - Added `V080Tests.cs` (7 unit tests covering LSP transport framing, initialization capabilities, semantic diagnostics, completions, hover docs, document symbol hierarchy, and shutdown lifecycle).
  - Total test count expanded to **218 tests** (200 unit tests + 18 integration tests) with a **100% pass rate**.

---

## [0.7.0] — 2026-10-26

### Added
- **Durable Agent Execution & Suspension Engine (`AgentLang.VM.Durable`)**:
  - `DurableExecutionEngine`: Long-running agent execution lifecycle with pause/suspension (`Suspend`), state persistence, and deterministic resumption (`ResumeAsync`).
  - `DurableExecutionContext`: Immutable state snapshots capturing execution status (`Pending`, `Running`, `Suspended`, `Completed`, `Faulted`), suspension reason (`WaitForSignal`, `HumanApprovalRequired`, `TimerExpired`, `ExternalWebhook`), waiting signals, step index, variables, agent histories, and received signals.
  - `IDurableStateStore`: Pluggable state stores with `MemoryDurableStateStore` and `FileDurableStateStore` (persisted under `.agent/durable/`).
  - Signal-based wakeups: Resumes suspended workflows upon receiving matching `ExecutionSignal` without losing memory or execution graph progress.
  - Seamlessly exposed on `AgentVirtualMachine.Durable`.
- **HTTP Web Microservice Gateway & Server (`agent serve --http [port]`) (`AgentLang.VM.Server`)**:
  - `AgentHttpServer`: High-performance, zero-dependency async HTTP microservice server built on .NET `HttpListener`.
  - Configurable port support (`agent serve --http [port]` or `--port <number>`, default `8080`).
  - Standard REST & SSE endpoints:
    - `POST /api/v1/run`: Executes code and returns JSON execution status, output, and variables.
    - `POST /api/v1/stream`: Real-time streaming via Server-Sent Events (SSE) emitting token and execution events.
    - `POST /api/v1/resume`: Resumes suspended durable workflows with signal payloads.
    - `GET /api/v1/status`: Health check, active execution telemetry, and durable workflow overview.
    - `GET /api/v1/durable`: Queries active, suspended, and completed durable executions.
  - **Embedded Web Playground (`/` and `/playground`)**: Modern, responsive dark-mode interactive web dashboard for writing `.agt` code, triggering runs, streaming SSE events, and inspecting durable workflows.
- **Testing**:
  - Added `V070Tests.cs` (7 comprehensive unit and integration tests covering start/complete, suspend/resume across steps, file persistence/reloading, HTTP server status/playground, code execution endpoint, and HTTP-driven workflow resumption).
  - Total test count expanded to **211 tests** (193 unit tests + 18 integration tests) with 100% pass rate.

---

## [0.6.9] — 2026-10-26

### Added
- **First-Class Native Schema Declarations (`schema`)**:
  - Added native `schema` keyword, Lexer token (`TokenType.Schema`), AST nodes (`SchemaDeclarationNode`, `SchemaFieldNode`), and Parser support.
  - Declarative typed contract fields (`string`, `int`, `float`, `bool`, objects) with array support (`[type]`).
  - In-line constraints: `min`, `max`, `regex`, and `required`.
  - Semantic analysis validation with duplicate field detection diagnostic (`AL0038`).
  - Automatic AST-to-Runtime compilation via `SchemaAstExtensions.ToStructuredSchema()`.
  - Scoped runtime registration via `AgentLangRuntime.SchemaDefs`, making schemas directly usable for structured agent outputs.
- **Smart Cascading LLM Gateway & Circuit Breaker (`AgentLang.Models.LlmGatewayRouter`)**:
  - `LlmGatewayRouter`: Automatic model failover with ordered priority fallback chains across different providers (OpenAI, Anthropic, Gemini, Ollama, custom).
  - `ProviderHealth` telemetry: Tracks request counts, token metrics, latency averages, and consecutive failure counts.
  - Built-in 3-state Circuit Breaker (`Closed`, `Open`, `HalfOpen`) with configurable failure thresholds and cooldown periods to eliminate rate-limit cascades.
  - Exposed directly on `AgentVirtualMachine.Gateway`.
- **First-Class Native Local LLM Provider (`OllamaModelProvider`)**:
  - Native integration with Ollama for zero-cost, private, offline, air-gapped agent operations.
  - Auto-handling of popular open-weights models (`llama3`, `mistral`, `deepseek`, `qwen`, `phi`, `gemma`, `codellama`, and `ollama/*`).
  - Full parameter mapping (`temperature`, `system`, `prompt`) and token usage tracking.
  - Registered by default in `ModelRegistry`.
- **Testing**:
  - Added `V069Tests.cs` (12 comprehensive unit tests covering Native Schema AST parsing, Semantic checks, StructuredSchema conversions, Ollama local provider, LLM Gateway cascading fallback, Circuit Breaker state transitions, and Runtime integration).
  - Total test count expanded to **204 tests** (186 unit tests + 18 integration tests) with 100% pass rate.

---

## [0.6.8] — 2026-10-24

### Added
- **Hierarchical Task Planning & Decoupled Execution Engine (`AgentLang.VM.Planning`)**:
  - `PlanAndExecuteEngine`: Decouples macro-strategy generation from step execution. Manages directed dependency graphs (`TaskPlan`, `PlanStep`, `DependsOn`) with concurrent resolution of independent steps.
  - Dynamic `Replanner`: Catches runtime tool failures and dynamically synthesizes updated downstream steps without restarting long-horizon workflows.
  - `ReWooEngine` (Reasoning Without Observation): 3-phase execution architecture (Planner -> Worker -> Solver) using variable placeholders (`#E1`, `#E2`) for deterministic tool execution, reducing token consumption by up to 5x.
  - `TreeOfThoughtsEngine`: Multi-branch candidate exploration using Beam Search, heuristic scoring, and path pruning.
  - CLI Command: `agent plan "<goal>" [--tools <t1,t2>] [--execute]` for generating and executing task plan DAGs.
- **Multi-Modal Native Artifact Pipeline (`AgentLang.VM.Artifacts`)**:
  - `Artifact`: First-class immutable data entity with unique ID, MIME type, raw binary/text payloads, SHA-256 content hashes, and version lineages (`ParentArtifactId`).
  - `ArtifactStore`: Thread-safe, content-addressed storage managing text, JSON, and binary artifacts with automatic versioning and persistence under `.agent/artifacts/`.
- **Data Loss Prevention (DLP) & Fine-Grained Resource Access Control (`AgentLang.Security.Policy`)**:
  - `DataLossPrevention`: Automated scanning and redacting of sensitive credentials (OpenAI/Anthropic keys, GitHub tokens, AWS access keys, private keys, credit cards, bearer tokens, passwords, and emails).
  - Actions: `Redact` (`[REDACTED:...]`), `Mask` (`sk-***123`), and `Block` (throws `DlpSecurityViolationException`).
  - `ResourceAccessPolicy`: Path boundary traversal defense (`../` escaping) and domain whitelisting with wildcard support (`*.github.com`).
  - CLI flag: `agent run <file.agt> --dlp` to sanitize console output and execution logs.
- **Deterministic Testing Cassette System (VCR / Tool Replay) (`AgentLang.VM.Testing`)**:
  - `ToolCassette`: Records real tool executions and outputs into versioned JSON cassettes (`.agent/cassettes/`).
  - `CassetteRecorder`: Operates in `Record`, `Replay`, and `Auto` modes. Enables 100% deterministic, zero-cost, offline CI/CD regression testing for AI agents.
- **Testing**:
  - Added `V068Tests.cs` (14 comprehensive unit tests covering Plan-and-Execute, ReWOO, Tree of Thoughts, Artifacts, DLP scanning/redaction/blocking, Resource policies, and Cassette VCR replay).
  - Total test count increased to **192 tests** (174 unit tests + 18 integration tests), with a 100% pass rate.

---

## [0.6.7] — 2026-10-24

### Added
- **Graph-Based Workflow Orchestration (`AgentLang.VM.Graphs`)**:
  - `StateGraph<TState>` & `StateGraph`: Directed state graph execution runtime for building complex multi-step, cyclic, and parallel agent workflows.
  - Node registration with async cancellation-aware handlers (`AddNode`).
  - Conditional edge routing with router functions and explicit branch mappings (`AddConditionalEdge`).
  - Parallel fan-out and fan-in execution with custom join reducers (`AddParallelBranch`).
  - Cyclic refinement loops with `MaxCycles` safety guards and `StateGraphCycleLimitException` against runaway infinite execution.
  - Human-in-the-Loop (HITL) checkpoints with `InterruptBefore` and `InterruptAfter`, allowing execution inspection, state modification, and resumption (`ResumeAsync`).
  - Interactive Mermaid flowchart export (`ToMermaid()`) for direct rendering in documentation and UI.
- **Episodic & Entity Graph Memory (`AgentLang.VM.Memory`)**:
  - `EntityGraphMemory`: Knowledge graph memory engine storing entities, attributes, relations (`Source`, `Relation`, `Target`), confidence scores, and episodic interaction history.
  - Automated natural language relation extraction (`ExtractAndStoreFacts`) identifying concepts and relationships across sentences.
  - Multi-hop BFS subgraph exploration (`QuerySubgraph`) with configurable traversal depth.
  - Contextual knowledge recall (`RecallContext`) dynamically synthesizing relevant entity relationships into agent system prompts.
  - Full JSON persistence with `ExportJson()` and `ImportJson()`.
- **Strict Structured Schema Engine & Parser (`AgentLang.VM.Schemas`)**:
  - `StructuredSchema` & `SchemaBuilder`: Fluent API to declare strictly typed output schemas with types (`String`, `Integer`, `Float`, `Boolean`, `Array`, `Object`), constraints (`min`, `max`, `regex`), and descriptions.
  - Standard JSON Schema compiler (`ToJsonSchema()`) generating Draft 2020-12 specifications for LLM function calling and structured outputs.
  - `StructuredOutputParser`: Validates raw LLM responses against schemas with markdown code-fence extraction (`ExtractJson`) and constraint checking.
  - Autonomous `AutoRepair`: Self-corrects common LLM JSON syntax failures (trailing commas, unquoted object keys, single quotes, and missing closing braces/brackets).
- **Streaming Event Engine & CLI Streaming (`AgentLang.VM.Streaming`)**:
  - `AgentStreamChannel`: High-throughput async event channel producing `IAsyncEnumerable<AgentStreamEvent>` with backpressure.
  - Event types: `Token`, `Thought`, `ToolCallStart`, `ToolCallResult`, `NodeTransition`, `StatusChange`, `Error`, and `Complete`.
  - W3C Server-Sent Events (SSE) compliant formatter (`ToSseString()`).
  - `AgentVirtualMachine.StreamExecuteAsync`: Real-time streaming execution with step-by-step reasoning emission.
  - CLI flag: `agent run <file.agt> --stream` (or `-s`) for real-time streaming execution output in terminal and web consoles.
- **Testing**:
  - Added `V067Tests.cs` (19 comprehensive unit tests covering StateGraph cycles, branch routing, HITL interrupts, fan-out/fan-in reducers, Mermaid generation, entity extraction, graph recall, schema validation/repair, and streaming events).
  - Total tests increased to 178 tests (160 unit tests + 18 integration tests), 100% passing.

---

## [0.6.6] — 2026-10-24

### Added
- **Bytecode Intermediate Representation (IR) Engine (`AgentLang.VM.Bytecode`)**:
  - `AgentOpCode`: Full stack-based instruction set (`PushConst`, `LoadVar`, `StoreVar`, `Add`, `Sub`, `Mul`, `Div`, `CompareEq`, `Jump`, `JumpIfTrue`, `JumpIfFalse`, `CallAgent`, `CallTool`, `Print`, `Return`, `Halt`).
  - `AgentBytecodeModule`: Constant pool, symbol table, instruction stream, and binary `.agtc` serialization (`ToBytes` / `FromBytes`).
  - `BytecodeCompiler`: Compiles AST (`ProgramNode`) directly into optimized bytecode binaries.
  - `BytecodeExecutor`: High-speed virtual machine stack execution loop inside `AgentVirtualMachine`.
  - CLI: `agent compile <file.agt> [-o <out.agtc>]` and direct binary execution `agent run <file.agtc>`.
- **Hybrid Semantic Search & Semantic Prompt Caching (`AgentLang.VM.Memory`)**:
  - `HybridVectorStore`: Combines Dense vector embeddings with Sparse lexical search (BM25) using Reciprocal Rank Fusion (RRF).
  - `SemanticPromptCache`: High-speed fuzzy semantic caching with configurable similarity threshold (default `0.80`), cutting redundant LLM token costs.
- **Auto-Remediation & Self-Healing Loops (`AgentLang.VM.Resilience`)**:
  - `AutoRemediator`: Autonomous tool error recovery engine with `RemediationStrategy` (`InitialAttempt`, `RetryWithBackoff`, `FallbackTool`, `ReflectorRepair`).
  - `SelfHealingPolicy`: Configurable max attempts, tool fallback hierarchies, and argument reflector repair callbacks.
- **OpenAPI Dynamic Tool Generator (`AgentLang.Tools`)**:
  - `OpenApiToolGenerator`: Dynamically synthesizes native `CustomApiTool` instances from OpenAPI 3.0 / Swagger JSON specifications and registers them into `ToolRegistry`.
- **Multi-Agent Evaluation & Benchmark Harness (`AgentLang.VM.Eval`)**:
  - `BenchmarkDataset`: Test datasets loaded from JSON/JSONL with inputs, expected outputs, expected tool calls, and criteria.
  - `AgentBenchmarkRunner`: Runs agent evaluation, measures latency, tool call precision, and semantic similarity, producing markdown scorecard reports.
  - CLI command: `agent eval <file.agt> --dataset <data.json> [--agent <name>]`.
- **Testing**:
  - Added `V066Tests.cs` (8 comprehensive tests covering bytecode compilation/execution, hybrid search, semantic caching, auto-remediation, OpenAPI generation, and evaluation). Total: 141 unit tests + 18 integration tests = 159 tests (100% pass rate).

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
