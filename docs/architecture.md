# AgentLang Compiler and Runtime Architecture

## 1. High-Level Flow

```text
Source Code (.agent)
       │
       ▼
 ┌───────────┐
 │   Lexer   │  SourceText -> Tokens with line/column Spans
 └─────┬─────┘
       │
       ▼
 ┌───────────┐
 │  Parser   │  Tokens -> Typed AST (Declarations, Statements, Expressions)
 └─────┬─────┘
       │
       ▼
 ┌───────────┐
 │  Semantic │  Symbol Resolution, Type Checks, Levenshtein Suggestions
 └─────┬─────┘
       │
       ▼
 ┌───────────┐
 │  Runtime  │  Execution Context, Agent Lifecycle, Task Scheduler
 └─────┬─────┘
       ├── Security Engine (Zero-Trust Permission Checks)
       ├── Tool Registry (Filesystem, Browser, Terminal, HTTP, Calculator)
       ├── Model Registry (Mock, OpenAI, Gemini, Anthropic)
       ├── Event Bus (Task completion hooks)
       └── Parallel Executor (Concurrent MultiAgent processing)
```

---

## 2. Multi-Project Structure

- `AgentLang.AST`: Base nodes, visitors, source locations, and rich diagnostics formatting.
- `AgentLang.Lexer`: High-performance lexical scanner.
- `AgentLang.Parser`: Pratt & recursive descent parser with error recovery.
- `AgentLang.Semantic`: Scopes and symbol tables with Levenshtein typo detection.
- `AgentLang.Security`: Capability policies, permission matching, and human approval providers.
- `AgentLang.Models`: AI provider abstractions and HTTP clients.
- `AgentLang.Tools`: Sandboxed execution engine for system capabilities.
- `AgentLang.Runtime`: State, scope chains, operations, agents, and parallel concurrency.
- `AgentLang.Cli`: Developer command-line interface.
