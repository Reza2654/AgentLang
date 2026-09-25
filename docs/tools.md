# AgentLang Built-in Tools

AgentLang provides built-in tools that agents can request via `tools = [...]`:

## 1. `browser`
Web search and URL content fetching.
- `browser.search`: Queries web search engines or deterministic mock index.
  - Arguments: `query` (string)
- `browser.fetch`: Retrieves raw page content from a given URL.
  - Arguments: `url` (string)

## 2. `filesystem`
Safe, permission-controlled filesystem interaction.
- `filesystem.read`: Reads text content from a file.
  - Arguments: `path` (string)
- `filesystem.write`: Writes text content to a destination file.
  - Arguments: `path` (string), `content` (string)
- `filesystem.list`: Lists directory contents.
  - Arguments: `path` (string)
- `filesystem.exists`: Checks if a file or directory exists.
  - Arguments: `path` (string)

## 3. `terminal`
System command execution (strictly guarded by `cannot` by default).
- `terminal.run`: Runs a shell command.
  - Arguments: `command` (string)

## 4. `http`
Direct HTTP REST requests.
- `http.get`: Sends an HTTP GET request.
  - Arguments: `url` (string)

## 5. `calculator`
Mathematical evaluations.
- `calculator.eval`: Evaluates expressions.
  - Arguments: `expr` (string)
