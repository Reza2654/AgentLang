# AgentLang CLI Reference

The `agent` CLI manages the full developer lifecycle.

## Commands

### `agent doctor`
Runs health checks against the execution environment:
- Verifies .NET 10 installation
- Tests compiler and semantic validation
- Probes security sandbox
- Audits registered tools and model providers

### `agent new <project-name>`
Scaffolds a new project with the standard file structure:
- `project.json`
- `main.agent`
- `agents/researcher.agent`
- `permissions/default.permission`

### `agent run <file.agent> [flags]`
Parses, semantically validates, and executes an AgentLang program.
Flags:
- `--yes`, `-y`: Automatically approve all operations requiring human approval.

### `agent build <file.agent>`
Compiles and semantically validates code without executing it. Emits rich diagnostics with line/column and source snippet pointers (`^^^`).

### `agent install <package> [version]`
Installs a package dependency into `project.json`.

### `agent remove <package>`
Removes a package dependency from `project.json`.

### `agent list`
Lists all declared package dependencies.

### `agent --version`
Displays installed AgentLang and .NET versions.
