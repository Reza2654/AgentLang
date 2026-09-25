# AgentLang Security Architecture

## 1. Zero-Trust Philosophy

In conventional AI agent frameworks, safety is often implemented via prompt engineering (e.g., instructing the LLM: *"Do not delete files"*). This approach is fundamentally vulnerable to prompt injection, jailbreaking, and hallucination.

**AgentLang enforces security deterministically at the runtime layer.**
- AI models are never treated as a security boundary.
- All tool and system operations must be explicitly authorized against the agent's assigned `permission` policy.
- If an operation is forbidden, the runtime halts execution before any system call occurs.

---

## 2. Permission Modes

| Mode | Semantics | Runtime Behavior |
| :--- | :--- | :--- |
| `allow` | Authorized | Executes capability immediately |
| `ask` | Human-in-the-Loop | Halts execution, prompts operator for approval |
| `cannot` | Forbidden | Throws `SecurityException`, aborts operation |

---

## 3. Capability Matching Hierarchy

Permissions are evaluated in order of specificity:
1. **Exact match**: `browser.search` matches capability `browser.search`.
2. **Namespace prefix**: `filesystem` matches `filesystem.read`, `filesystem.write`, `filesystem.list`.
3. **Global wildcard**: `*` matches all capabilities.
4. **Default fallback**: Unspecified capabilities default to `cannot`.

Example:
```agentlang
permission BalancedPolicy {
    allow browser.search
    allow browser.fetch
    ask filesystem.write
    cannot terminal
}
```

---

## 4. Human Approval System

When an operation triggers an `ask` rule, the runtime delegates to an `IApprovalProvider`:

- **Interactive Console Mode** (`ConsoleApprovalProvider`):
  Prompts the user directly in the terminal:
  ```text
  [SECURITY APPROVAL REQUIRED]
  Agent:      AdminAgent
  Operation:  filesystem.write
  Details:    path=output/report.txt, content=...
  Approve execution? [y/N]:
  ```
- **Automated/CI Mode** (`AutoApprovalProvider`):
  Used in automated testing or when `--yes` / `-y` flag is supplied to `agent run`.
