# AgentLang VS Code Extension

Official Visual Studio Code extension providing first-class Language Server Protocol (LSP) and syntax highlighting for **AgentLang** (`.agt` / `.agent`).

---

## 🌟 Features

- **Syntax Highlighting**: Declarative agents, schemas, workflows, pipelines, models, and constraints.
- **Real-Time Diagnostics**: Instant compiler feedback as you type (Lexer, Parser, and Semantic Analyzer errors).
- **IntelliSense Auto-Completion**: Context-aware keywords, defined schemas and agents, tools, and popular LLM model identifiers.
- **Rich Hover Documentation**: Interactive markdown docstrings for language keywords and symbols.
- **Document Outline & Breadcrumbs**: Hierarchical symbol structure of agents, schemas, fields, and workflows.

---

## 🚀 Getting Started

The extension communicates with the built-in `agent lsp` binary over standard I/O:

```bash
# Verify the AgentLang CLI is on your PATH:
agent --version
# Should output: AgentLang v0.8.0
```

Open any `.agt` file to activate language services automatically.
