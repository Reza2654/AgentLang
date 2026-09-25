# AgentLang Language Specification — v0.1.0-alpha

## 1. Syntax Overview

AgentLang source files use the `.agent` extension.

### 1.1 Comments
```agentlang
// Single line comment
# Shell-style comment
/* Multi-line
   comment */
```

### 1.2 Entry Point
```agentlang
main {
    agent Researcher
}
```
Statements in `main` execute sequentially from top to bottom.

---

## 2. Declarations

### 2.1 Agent Declaration
```agentlang
agent <Identifier> [ ( <ConfigKey> = <Value>, ... ) ] {
    [ <ContextDeclaration> | <TaskDeclaration> | <Statement> ]*
}
```

Supported config keys:
- `model`: Model identifier or name (e.g. `GPT`, `Claude`, `Gemini`, `"gpt-4o"`, `"mock"`)
- `permission`: Reference to a declared `permission` policy
- `tools`: List of tools accessible to this agent, e.g. `[browser, filesystem]`
- `memory`: When set to `null`, disables agent memory retention

### 2.2 Task Declaration
```agentlang
task <Identifier> {
    [ <Statement> ]*
    [ return <Expression> ]
}
```

### 2.3 Context Declaration
```agentlang
context <Identifier> = <Expression>
```
Declares agent context. Automatically included in agent memory when memory is enabled.

### 2.4 Permission Declaration
```agentlang
permission <Identifier> {
    [ allow | ask | cannot ] <CapabilityPath>
}
```
Where `<CapabilityPath>` is a dotted identifier or wildcard (e.g. `browser.search`, `filesystem.write`, `terminal`, `*`).

### 2.5 MultiAgent Declaration
```agentlang
multiagent <Identifier> {
    [ <AgentDeclaration> | <ParallelBlock> | <Statement> ]*
}
```

### 2.6 Parallel Block
```agentlang
parallel {
    [ <AgentDeclaration> | <Statement> ]*
}
```
Executes nested agents concurrently using async task coordination.

### 2.7 Event Declaration
```agentlang
event <Target> {
    [ <Statement> ]*
}
```
Subscribes to lifecycle events such as `<taskName>.finished`.

---

## 3. Statements

- **Assignment**: `variableName = expression`
- **Conditionals**: `if <expr> { ... } else { ... }`
- **Loops**:
  - `while <expr> { ... }`
  - `repeat <expr> { ... }`
  - `for <item> in <iterable> { ... }`
- **Return**: `return <expr>`
- **Error Handling**: `try { ... } catch <ident> { ... }`
- **Retry**: `retry <count> { ... }`
- **Agent Invocation**: `agent <AgentName>`

---

## 4. Expressions & Operations

- **Literals**: Numbers (`42`, `3.14`), Strings (`"hello"`), Booleans (`true`, `false`), `null`, Lists (`[1, 2, 3]`), Maps (`{ "key": "val" }`).
- **Operators**: `+`, `-`, `*`, `/`, `%`, `==`, `!=`, `<`, `<=`, `>`, `>=`, `and`/`&&`, `or`/`||`, `not`/`!`.
- **AI Operations**:
  - `think(prompt)`: Invokes the agent's AI model. Returns an `OperationValue`.
  - `research(prompt)`: Conducts automated research using tools and model. Returns an `OperationValue`.
- **Member Access**:
  - `operation.result`: Returns evaluated string or object from an operation.
  - `task.result`: Returns the return value of a task.
  - `AgentName.taskName.result`: Cross-agent result reference.
- **Built-in Functions**:
  - `print(...)`: Outputs values to stdout.
  - `input(prompt)`: Reads user input from stdin.
