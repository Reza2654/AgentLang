# Changelog

All notable changes to **AgentLang** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-09-25

### Added
- **First-Class Functions**: Define reusable functions with `function name(params) { ... }`, return values (`return expr`), lexical scoping/closures, and recursion call stack depth guard (limit: 256 calls, error: `AGT301`).
- **Extended Type System**: Full support for 11 runtime types: `string`, `number`, `boolean`, `list`, `map`, `null`, `agent`, `task`, `operation`, `function`, `tool`.
- **Index Access & Introspection**: Bracket index access on lists (`arr[0]`), maps (`dict["key"]`), strings (`str[0]`), and agents (`agent["inbox"]`), plus the built-in `type(expr)` function.
- **Model System 2.0**:
  - Model aliases via `model <alias> = <target>` declarations.
  - Task-level model overrides: `task analyze (model = fast) { ... }`.
  - Automatic model fallback chain and alias resolution.
- **First-Class Custom Tools**: Declare native tools directly in `.agent` scripts with `tool <name> { description = "..." input <param>: <type> execute { ... } }`, fully integrated with zero-trust permissions.
- **Agent Communication & Messaging**:
  - Push-based asynchronous messaging: `send <expr> to <Agent> [tag <expr>]`.
  - Agent `inbox` inspection: read received messages, tags, senders, and timestamps.
- **Persistent Memory Foundation**:
  - `IPersistentMemoryStore` abstraction.
  - `LocalFileMemoryStore` with automatic JSON snapshotting in `.agentlang/memory/{agent}.json`.
  - `InMemoryMemoryStore` for zero-IO testing.
- **Enriched Operation Telemetry**: `OperationValue` now exposes `.status`, `.result`, `.model`, `.duration`, `.error`, `.type`, and `.toolCalls`.
- **Structured Error Hierarchy (`AGTxxx`)**: Standardized machine-readable error codes:
  - `AGT100` (`AgentLangSyntaxException`): Syntax and parsing errors.
  - `AGT200` (`AgentLangSemanticException`): Semantic analysis, undeclared symbols, type mismatches.
  - `AGT300` / `AGT301` / `AGT302` (`AgentLangRuntimeException`): Runtime errors, recursion overflow, index bounds.
  - `AGT400` (`AgentLangModelException`): LLM provider errors and timeouts.
  - `AGT500` (`AgentLangToolException`): Tool execution errors and invalid arguments.
  - `AGT600` / `AGT601` (`AgentLangPermissionException` / `AgentLangApprovalException`): Security policy violations and denied approvals.
  - `AGT700` / `AGT701` (`AgentLangNetworkException` / `AgentLangTimeoutException`): Network and timeout exceptions.
- **New CLI Commands**:
  - `agent repl`: Interactive REPL for quick prototyping and testing expressions.
  - `agent check [path]`: Fast syntax and semantic static verification without execution.
  - `agent format [path] [--dry-run]`: Code formatter with deterministic AST pretty-printing.
  - `agent test [path]`: Automated test runner for executing `.agent` test suites.
- **6 New Real-World Examples**:
  - `examples/functions/`: Functions, recursion, and type introspection.
  - `examples/custom-tools/`: Custom in-script tool definitions with input parameters.
  - `examples/agent-communication/`: Inter-agent message sending and inbox processing.
  - `examples/model-overrides/`: Model aliases and task-level overrides.
  - `examples/persistent-memory/`: Long-term agent memory persistence across executions.
  - `examples/structured-errors/`: Typed exceptions and error code handling.

### Changed
- CLI version upgraded to `0.2.0`.
- Runtime `SecurityException` now inherits from `AgentLangPermissionException` with code `AGT600`.
- Top-level declarations now inherit from `StatementNode`, enabling uniform parsing and validation.

## [0.1.0-alpha] - 2026-09-25


### Added
- **Core Language Specification**: First-class `agent`, `multiagent`, `task`, `context`, `permission`, and `event` constructs.
- **Lexer & Parser**: Recursive-descent Pratt parser with source spans and error recovery.
- **Semantic Analyzer**: Scope resolution, symbol tables, and Levenshtein typo suggestion (`did you mean '...'?`).
- **Security Engine**: Zero-Trust permission evaluation (`allow`, `ask`, `cannot`) and interactive human approval.
- **AI Operations**: Native `think(...)` and `research(...)` operations.
- **Operation vs. Result**: Explicit distinction between `operation` state and `operation.result` payloads.
- **MultiAgent Orchestration**: Sequential and concurrent execution with `parallel { ... }`.
- **Event System**: EventBus for reactive workflows (`event <task>.finished`).
- **Resilience**: `try / catch` statements and `retry N { ... }` loops with exponential backoff.
- **Tool System**: Built-in `filesystem`, `browser`, `terminal`, `http`, and `calculator` tools.
- **Model Providers**: Offline deterministic `MockModelProvider` and REST integrations for OpenAI, Gemini, and Anthropic.
- **CLI**: `agent new`, `agent run`, `agent build`, `agent doctor`, `agent install`, `agent remove`, `agent update`, `agent list`, and `agent --version`.
- **Examples**: 10 complete executable examples covering all language features.
- **Testing**: 40 unit and integration tests with 100% pass rate.
