# Changelog

All notable changes to **AgentLang** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
