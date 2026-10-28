using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.Security;
using AgentLang.Security.Policy;
using AgentLang.Transpiler;
using AgentLang.VM;
using AgentLang.VM.Bytecode;
using AgentLang.VM.Checkpointing;
using AgentLang.VM.Eval;
using AgentLang.VM.MCP;
using AgentLang.VM.Observability;
using AgentLang.VM.Planning;
using AgentLang.VM.Lsp;
using AgentLang.VM.Server;
using AgentLang.VM.Streaming;

namespace AgentLang.Cli;

public static class Program
{
    public const string Version = "0.8.1";

    public static async Task<int> Main(string[] args)
    {
        EnvLoader.Load();

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine($"AgentLang v{Version} (.NET {Environment.Version} / C# 14)");
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "repl" => await ReplCommand.RunAsync(),
                "chat" => await ChatCommand.RunAsync(args),
                "check" => CheckCommand.Run(args),
                "format" => FormatCommand.Run(args),
                "test" => await TestCommand.RunAsync(args),
                "doctor" => await DoctorCommand.RunAsync(),
                "new" => HandleNew(args),
                "run" => await HandleRunAsync(args),
                "build" => HandleBuild(args),
                "transpile" => HandleTranspile(args),
                "install" => HandleInstall(args),
                "remove" => HandleRemove(args),
                "update" => HandleUpdate(args),
                "list" => HandleList(args),
                "pack" => HandlePack(args),
                "docker" => HandleDocker(args),
                "serve" => await HandleServeAsync(args),
                "checkpoint" => await HandleCheckpointAsync(args),
                "compile" => await HandleCompileAsync(args),
                "eval" => await HandleEvalAsync(args),
                "plan" => await HandlePlanAsync(args),
                "lsp" => await HandleLspAsync(args),
                "watch" => await WatchCommand.RunAsync(args),
                _ => HandleUnknown(args[0])
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nFatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"AgentLang CLI — v{Version}");
        Console.WriteLine("Agent-Native Programming Language for Autonomous AI Systems\n");
        Console.ResetColor();

        Console.WriteLine("Usage: agent <command> [options] [arguments]\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  repl                  Start interactive REPL shell");
        Console.WriteLine("  chat <file> [agent]   Interactive live terminal chat with an autonomous agent");
        Console.WriteLine("  run <file> [flags]    Execute an AgentLang program (.agt / .agent)");
        Console.WriteLine("  check [target]        Fast syntax and semantic static checker");
        Console.WriteLine("  format [target] [-w]  Format AgentLang source code");
        Console.WriteLine("  test [target]         Run AgentLang test suites");
        Console.WriteLine("  build <file> [flags]  Parse, validate, and optionally compile/transpile an AgentLang program");
        Console.WriteLine("  transpile <file>      Transpile AgentLang (.agt) to Python or TypeScript");
        Console.WriteLine("  new <name>            Scaffold a new AgentLang project");
        Console.WriteLine("  doctor                Diagnose environment, runtime, models, tools, and security");
        Console.WriteLine("  install <pkg> [ver]   Install a package dependency");
        Console.WriteLine("  remove <pkg>          Remove a package dependency");
        Console.WriteLine("  update <pkg>          Update a package dependency");
        Console.WriteLine("  list                  List project dependencies");
        Console.WriteLine("  pack [dir] [-o <file>] Bundle an Agent project into a distributable .agtpkg archive");
        Console.WriteLine("  docker [dir] [entry]  Generate production Dockerfile and .dockerignore for micro-agent");
        Console.WriteLine("  serve [flags]         Start an MCP server (--mcp), HTTP Gateway (--http [port]), or LSP server (--lsp)");
        Console.WriteLine("  lsp                   Start standard Language Server Protocol (LSP) server over stdio");
        Console.WriteLine("  watch <file> [--run]  Watch a script for changes and perform hot-reload verification/execution");
        Console.WriteLine("  checkpoint [cmd]      Inspect or list resumable workflow execution checkpoints");
        Console.WriteLine("  compile <file> [-o]   Compile an AgentLang script into an optimized .agtc bytecode binary");
        Console.WriteLine("  eval <file> [options] Evaluate an agent on a benchmark dataset and generate a scorecard");
        Console.WriteLine("  plan <goal> [flags]   Generate or execute a hierarchical multi-step task plan DAG");
        Console.WriteLine("  --version, -v         Display AgentLang version");
        Console.WriteLine("  --help, -h            Show this help documentation\n");
        Console.WriteLine("Flags for 'run':");
        Console.WriteLine("  --yes, -y             Automatically approve operations requiring human approval");
        Console.WriteLine("  --trace, -t           Record and print distributed OpenTelemetry execution trace tree");
        Console.WriteLine("  --stream, -s          Stream execution events and tokens in real time (SSE compatible)");
        Console.WriteLine("  --dlp                 Mask or redact sensitive API keys, tokens, and credentials (DLP)");
        Console.WriteLine("Flags for 'format':");
        Console.WriteLine("  --write, -w           Write formatted output directly to files");
        Console.WriteLine("  --check, -c           Check if files are formatted without modifying them");
    }

    private static int HandleNew(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing project name. Usage: agent new <project-name>");
            Console.ResetColor();
            return 1;
        }

        string projectName = args[1];
        string cwd = Directory.GetCurrentDirectory();
        ProjectManager.ScaffoldProject(projectName, cwd);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Created AgentLang project '{projectName}' at {Path.Combine(cwd, projectName)}");
        Console.ResetColor();
        Console.WriteLine($"\nTo get started:");
        Console.WriteLine($"  cd {projectName}");
        Console.WriteLine($"  agent run main.agt");
        return 0;
    }

    private static async Task<int> HandleRunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent run <file.agt>");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        bool autoApprove = args.Any(a => a is "--yes" or "-y");
        bool enableTrace = args.Any(a => a is "--trace" or "-t");
        bool enableStream = args.Any(a => a is "--stream" or "-s");
        bool enableDlp = args.Any(a => a is "--dlp");

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
        }

        // Handle pre-compiled bytecode (.agtc) execution
        if (filePath.EndsWith(".agtc", StringComparison.OrdinalIgnoreCase))
        {
            var module = AgentBytecodeModule.LoadFromFile(filePath);
            var vm = new AgentVirtualMachine();
            var result = await vm.ExecuteBytecodeAsync(module);
            if (enableTrace)
            {
                Console.WriteLine();
                await vm.Tracer.ExportAsync(new ConsoleTraceExporter());
            }
            return 0;
        }

        string source = await File.ReadAllTextAsync(filePath);
        var sourceText = new SourceText(source, filePath);

        // 1. Parse
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();
        if (parser.Diagnostics.HasErrors)
        {
            PrintDiagnostics(sourceText, parser.Diagnostics);
            return 1;
        }

        // 2. Semantic Analysis
        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);
        if (semantic.Diagnostics.HasErrors)
        {
            PrintDiagnostics(sourceText, semantic.Diagnostics);
            return 1;
        }

        // Print warnings if any
        if (parser.Diagnostics.HasWarnings || semantic.Diagnostics.HasWarnings)
        {
            PrintDiagnostics(sourceText, parser.Diagnostics);
            PrintDiagnostics(sourceText, semantic.Diagnostics);
        }

        // 3. Runtime execution
        IApprovalProvider approval = autoApprove ? new AutoApprovalProvider(true) : new ConsoleApprovalProvider();
        var secEngine = new SecurityEngine(approval);
        var runtime = new AgentLangRuntime(securityEngine: secEngine);

        if (enableStream)
        {
            var vm = new AgentVirtualMachine(runtime: runtime);
            var channel = new AgentStreamChannel();
            var streamTask = Task.Run(async () =>
            {
                await foreach (var ev in channel.ReadAllAsync())
                {
                    switch (ev.Type)
                    {
                        case AgentStreamEventType.Thought:
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"[thought] {ev.Content}");
                            Console.ResetColor();
                            break;
                        case AgentStreamEventType.Token:
                            Console.Write(ev.Content);
                            break;
                        case AgentStreamEventType.ToolCallStart:
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"[tool:call] {ev.Content}");
                            Console.ResetColor();
                            break;
                        case AgentStreamEventType.ToolCallResult:
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.WriteLine($"[tool:result] {ev.Content}");
                            Console.ResetColor();
                            break;
                        case AgentStreamEventType.Complete:
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"\n[complete] {ev.Content}");
                            Console.ResetColor();
                            break;
                        case AgentStreamEventType.Error:
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"\n[error] {ev.Content}");
                            Console.ResetColor();
                            break;
                        default:
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine($"[{ev.Type}] {ev.Content}");
                            Console.ResetColor();
                            break;
                    }
                }
            });

            await vm.StreamExecuteAsync(source, channel, filePath);
            await streamTask;
            return 0;
        }

        if (enableTrace)
        {
            var vm = new AgentVirtualMachine(runtime: runtime);
            await vm.ExecuteAsync(program);

            Console.WriteLine();
            await vm.Tracer.ExportAsync(new ConsoleTraceExporter());
            await vm.Tracer.ExportAsync(new JsonTraceExporter());
        }
        else
        {
            await runtime.ExecuteProgramAsync(program);
        }
        return 0;
    }

    private static int HandleBuild(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent build <file.agt> [--target python|ts] [-o <output>]");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
        }

        string source = File.ReadAllText(filePath);
        var sourceText = new SourceText(source, filePath);

        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);

        bool errors = parser.Diagnostics.HasErrors || semantic.Diagnostics.HasErrors;
        PrintDiagnostics(sourceText, parser.Diagnostics);
        PrintDiagnostics(sourceText, semantic.Diagnostics);

        if (errors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Build failed: verification encountered errors.");
            Console.ResetColor();
            return 1;
        }

        // Check if target is specified for code generation
        string? targetLang = null;
        string? outputFile = null;
        for (int i = 2; i < args.Length; i++)
        {
            if ((args[i] == "--target" || args[i] == "-t") && i + 1 < args.Length)
            {
                targetLang = args[++i].ToLowerInvariant();
            }
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[++i];
            }
        }

        if (args.Any(a => a is "--docker" or "-d"))
        {
            string projectDir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Directory.GetCurrentDirectory();
            var (df, di) = ProjectManager.GenerateDockerFiles(projectDir, Path.GetFileName(filePath));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Generated container deployment files:");
            Console.WriteLine($"  - {df}");
            Console.WriteLine($"  - {di}");
            Console.ResetColor();
        }

        if (!string.IsNullOrEmpty(targetLang))
        {
            return DoTranspile(program, filePath, targetLang, outputFile);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Build succeeded: '{filePath}' passed syntax and semantic verification.");
        Console.ResetColor();
        return 0;
    }

    private static int HandleDocker(string[] args)
    {
        string dir = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : Directory.GetCurrentDirectory();
        string entry = args.Length > 2 && !args[2].StartsWith('-') ? args[2] : "main.agt";

        try
        {
            var (df, di) = ProjectManager.GenerateDockerFiles(dir, entry);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Generated Docker container files in: {dir}");
            Console.WriteLine($"  - Dockerfile (Entrypoint: {entry})");
            Console.WriteLine($"  - .dockerignore");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error generating Docker configuration: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static async Task<int> HandleServeAsync(string[] args)
    {
        bool isHttp = args.Any(a => a.Equals("--http", StringComparison.OrdinalIgnoreCase));
        if (isHttp)
        {
            int port = 8080;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Equals("--http", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
                {
                    port = p;
                }
                else if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out int p2))
                {
                    port = p2;
                }
            }

            var vm = new AgentVirtualMachine();
            using var server = new AgentHttpServer(port, vm);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await server.StartAsync(cts.Token);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"AgentLang Microservice Server running at: http://localhost:{port}/");
            Console.WriteLine($"  - Playground:    http://localhost:{port}/playground");
            Console.WriteLine($"  - Execute API:   POST http://localhost:{port}/api/v1/run");
            Console.WriteLine($"  - Stream API:    POST http://localhost:{port}/api/v1/stream");
            Console.WriteLine($"  - Resume API:    POST http://localhost:{port}/api/v1/resume");
            Console.WriteLine($"  - Status API:    GET  http://localhost:{port}/api/v1/status");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("\nPress Ctrl+C to stop the server...\n");
            Console.ResetColor();

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Graceful cancellation
            }

            Console.WriteLine("Shutting down AgentLang server...");
            server.Stop();
            return 0;
        }

        bool isLsp = args.Any(a => a.Equals("--lsp", StringComparison.OrdinalIgnoreCase));
        if (isLsp)
        {
            return await HandleLspAsync(args);
        }

        bool isMcp = args.Any(a => a.Equals("--mcp", StringComparison.OrdinalIgnoreCase)) || args.Length == 1;
        if (isMcp)
        {
            using var transport = new StreamMcpTransport(Console.OpenStandardInput(), Console.OpenStandardOutput(), leaveOpen: true);
            var server = new McpServer(transport);
            await server.RunAsync();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Usage: agent serve [--mcp | --http [port] | --lsp]");
        Console.ResetColor();
        return 1;
    }

    private static async Task<int> HandleLspAsync(string[] _)
    {
        using var transport = new LspTransport(Console.OpenStandardInput(), Console.OpenStandardOutput(), leaveOpen: true);
        var server = new AgentLspServer(transport);
        await server.RunAsync();
        return 0;
    }

    private static async Task<int> HandleCheckpointAsync(string[] args)
    {
        var store = new FileCheckpointStore();
        if (args.Length < 2 || args[1] is "list")
        {
            var list = await store.ListAsync();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Found {list.Count} checkpoint(s):");
            Console.ResetColor();

            foreach (var cp in list)
            {
                Console.WriteLine($"  - [{cp.CheckpointId}] {cp.WorkflowName} (Step: {cp.StepIndex}, Status: {cp.Status}, Time: {cp.Timestamp:u})");
            }
            return 0;
        }

        if (args[1] is "show" or "inspect" && args.Length >= 3)
        {
            var cp = await store.GetAsync(args[2]);
            if (cp == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Checkpoint '{args[2]}' not found.");
                Console.ResetColor();
                return 1;
            }
            Console.WriteLine(cp.ToJson());
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Usage: agent checkpoint [list | show <id>]");
        Console.ResetColor();
        return 0;
    }

    private static int HandleTranspile(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent transpile <file.agt> [--target python|ts] [-o <output>]");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
        }

        string source = File.ReadAllText(filePath);
        var sourceText = new SourceText(source, filePath);

        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);

        bool errors = parser.Diagnostics.HasErrors || semantic.Diagnostics.HasErrors;
        PrintDiagnostics(sourceText, parser.Diagnostics);
        PrintDiagnostics(sourceText, semantic.Diagnostics);

        if (errors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ Transpilation failed due to syntax or semantic errors.");
            Console.ResetColor();
            return 1;
        }

        string targetLang = "python";
        string? outputFile = null;

        for (int i = 2; i < args.Length; i++)
        {
            if ((args[i] == "--target" || args[i] == "-t") && i + 1 < args.Length)
            {
                targetLang = args[++i].ToLowerInvariant();
            }
            else if ((args[i] == "-o" || args[i] == "--output") && i + 1 < args.Length)
            {
                outputFile = args[++i];
            }
        }

        return DoTranspile(program, filePath, targetLang, outputFile);
    }

    private static int DoTranspile(ProgramNode program, string sourceFile, string targetLang, string? outputFile)
    {
        bool isTs = targetLang is "ts" or "typescript";
        string code = isTs
            ? TranspilerEngine.TranspileToTypeScript(program)
            : TranspilerEngine.TranspileToPython(program);

        if (!string.IsNullOrEmpty(outputFile))
        {
            File.WriteAllText(outputFile, code);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Transpiled to {outputFile} ({(isTs ? "TypeScript" : "Python")})");
            Console.ResetColor();
        }
        else
        {
            string ext = isTs ? ".ts" : ".py";
            string defaultOut = Path.ChangeExtension(sourceFile, ext);
            File.WriteAllText(defaultOut, code);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Transpiled to {defaultOut} ({(isTs ? "TypeScript" : "Python")})");
            Console.ResetColor();
        }

        return 0;
    }

    private static int HandleInstall(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing package name. Usage: agent install <package> [version]");
            Console.ResetColor();
            return 1;
        }

        string pkg = args[1];
        string ver = args.Length > 2 ? args[2] : "^1.0.0";
        string cwd = Directory.GetCurrentDirectory();

        var manifest = ProjectManager.LoadManifest(cwd);
        manifest.Dependencies[pkg] = ver;
        ProjectManager.SaveManifest(cwd, manifest);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Installed {pkg}@{ver}");
        Console.ResetColor();
        return 0;
    }

    private static int HandleRemove(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing package name. Usage: agent remove <package>");
            Console.ResetColor();
            return 1;
        }

        string pkg = args[1];
        string cwd = Directory.GetCurrentDirectory();

        var manifest = ProjectManager.LoadManifest(cwd);
        if (manifest.Dependencies.Remove(pkg))
        {
            ProjectManager.SaveManifest(cwd, manifest);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Removed package {pkg}");
            Console.ResetColor();
            return 0;
        }

        Console.WriteLine($"Package '{pkg}' is not in project dependencies.");
        return 0;
    }

    private static int HandleUpdate(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing package name. Usage: agent update <package>");
            Console.ResetColor();
            return 1;
        }

        string pkg = args[1];
        string cwd = Directory.GetCurrentDirectory();

        var manifest = ProjectManager.LoadManifest(cwd);
        if (manifest.Dependencies.ContainsKey(pkg))
        {
            manifest.Dependencies[pkg] = "latest";
            ProjectManager.SaveManifest(cwd, manifest);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Updated {pkg} to latest");
            Console.ResetColor();
            return 0;
        }

        Console.WriteLine($"Package '{pkg}' is not in project dependencies.");
        return 0;
    }

    private static int HandleList(string[] _)
    {
        string cwd = Directory.GetCurrentDirectory();
        var manifest = ProjectManager.LoadManifest(cwd);

        Console.WriteLine($"Dependencies for {manifest.Name} (v{manifest.Version}):");
        if (manifest.Dependencies.Count == 0)
        {
            Console.WriteLine("  (no dependencies declared)");
            return 0;
        }

        foreach (var (k, v) in manifest.Dependencies)
        {
            Console.WriteLine($"  - {k}: {v}");
        }
        return 0;
    }

    private static int HandlePack(string[] args)
    {
        string projectDir = Directory.GetCurrentDirectory();
        string? output = null;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] is "--out" or "-o") && i + 1 < args.Length)
            {
                output = args[++i];
            }
            else if (!args[i].StartsWith('-'))
            {
                projectDir = args[i];
            }
        }

        try
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Packaging AgentLang project at: {projectDir}...");
            Console.ResetColor();

            string packagePath = ProjectManager.PackProject(projectDir, output);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Successfully bundled agent package: {packagePath}");
            Console.ResetColor();
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error packaging project: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static async Task<int> HandleCompileAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent compile <file.agt> [-o <out.agtc>]");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
        }

        string outFile = Path.ChangeExtension(filePath, ".agtc");
        int oIdx = Array.IndexOf(args, "-o");
        if (oIdx >= 0 && oIdx + 1 < args.Length)
        {
            outFile = args[oIdx + 1];
        }

        string source = await File.ReadAllTextAsync(filePath);
        var sourceText = new SourceText(source, filePath);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        if (parser.Diagnostics.HasErrors)
        {
            PrintDiagnostics(sourceText, parser.Diagnostics);
            return 1;
        }

        var compiler = new BytecodeCompiler();
        var module = compiler.Compile(program, Path.GetFileNameWithoutExtension(filePath));
        module.SaveToFile(outFile);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Compiled '{filePath}' to bytecode binary '{outFile}'");
        Console.WriteLine($"  - Instructions: {module.Instructions.Count}");
        Console.WriteLine($"  - Constants: {module.ConstantPool.Count}");
        Console.WriteLine($"  - Symbols: {module.SymbolTable.Count}");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> HandleEvalAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent eval <file.agt> --dataset <dataset.json> [--agent <name>]");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        string? datasetPath = null;
        string? targetAgentName = null;

        for (int i = 2; i < args.Length; i++)
        {
            if ((args[i] == "--dataset" || args[i] == "-d") && i + 1 < args.Length) datasetPath = args[++i];
            else if ((args[i] == "--agent" || args[i] == "-a") && i + 1 < args.Length) targetAgentName = args[++i];
        }

        if (string.IsNullOrEmpty(datasetPath) || !File.Exists(datasetPath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing valid --dataset file.");
            Console.ResetColor();
            return 1;
        }

        string source = await File.ReadAllTextAsync(filePath);
        var sourceText = new SourceText(source, filePath);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var runtime = new AgentLangRuntime();
        await runtime.ExecuteProgramAsync(program);

        var agent = targetAgentName != null && runtime.AgentInstances.TryGetValue(targetAgentName, out var a)
            ? a
            : runtime.AgentInstances.Values.FirstOrDefault();

        if (agent == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: No defined agent found in program to evaluate.");
            Console.ResetColor();
            return 1;
        }

        string datasetJson = await File.ReadAllTextAsync(datasetPath);
        var dataset = BenchmarkDataset.FromJson(datasetJson);

        var runner = new AgentBenchmarkRunner();
        var scorecard = await runner.RunBenchmarkAsync(agent, dataset, runtime);

        Console.WriteLine();
        Console.WriteLine(scorecard.ToMarkdownReport());
        return scorecard.PassRate >= 0.70 ? 0 : 1;
    }

    private static async Task<int> HandlePlanAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing objective. Usage: agent plan \"<goal-or-task>\" [--tools <t1,t2>] [--execute]");
            Console.ResetColor();
            return 1;
        }

        string goal = args[1];
        var tools = new List<string> { "web_search", "document_summary", "code_generator" };
        bool execute = args.Any(a => a is "--execute" or "-e");

        for (int i = 2; i < args.Length; i++)
        {
            if ((args[i] == "--tools" || args[i] == "-t") && i + 1 < args.Length)
            {
                tools = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        var planner = new HeuristicPlanner();
        var plan = await planner.CreatePlanAsync(goal, tools);

        if (!execute)
        {
            Console.WriteLine(plan.ToMarkdown());
            return 0;
        }

        Console.WriteLine(plan.ToMarkdown());
        Console.WriteLine("\nExecuting plan steps...");

        var engine = new PlanAndExecuteEngine(planner);
        var result = await engine.ExecuteAsync(goal, async (step, ct) =>
        {
            Console.WriteLine($"  → Running Step {step.StepId}: {step.Description}");
            await Task.Delay(50, ct);
            return $"Step {step.StepId} completed successfully";
        }, tools);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n✓ {result.Summary}");
        Console.ResetColor();
        return 0;
    }

    private static int HandleUnknown(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: Unknown command '{cmd}'. Use 'agent --help' to view available commands.");
        Console.ResetColor();
        return 1;
    }

    private static void PrintDiagnostics(SourceText sourceText, DiagnosticBag bag)
    {
        foreach (var diag in bag.Diagnostics)
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (diag.Severity == DiagnosticSeverity.Warning)
                Console.ForegroundColor = ConsoleColor.Yellow;
            else
                Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteLine(sourceText.FormatDiagnostic(diag));
            Console.ResetColor();
            Console.WriteLine();
        }
    }
}
