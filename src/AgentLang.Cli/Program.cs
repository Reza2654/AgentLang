using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using AgentLang.Security;

namespace AgentLang.Cli;

public static class Program
{
    public const string Version = "0.3.1";

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
                "check" => CheckCommand.Run(args),
                "format" => FormatCommand.Run(args),
                "test" => await TestCommand.RunAsync(args),
                "doctor" => await DoctorCommand.RunAsync(),
                "new" => HandleNew(args),
                "run" => await HandleRunAsync(args),
                "build" => HandleBuild(args),
                "install" => HandleInstall(args),
                "remove" => HandleRemove(args),
                "update" => HandleUpdate(args),
                "list" => HandleList(args),
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
        Console.WriteLine("  run <file> [flags]    Execute an AgentLang program (.agent)");
        Console.WriteLine("  check [target]        Fast syntax and semantic static checker");
        Console.WriteLine("  format [target] [-w]  Format AgentLang source code");
        Console.WriteLine("  test [target]         Run AgentLang test suites");
        Console.WriteLine("  build <file>          Parse and semantically validate an AgentLang program");
        Console.WriteLine("  new <name>            Scaffold a new AgentLang project");
        Console.WriteLine("  doctor                Diagnose environment, runtime, models, tools, and security");
        Console.WriteLine("  install <pkg> [ver]   Install a package dependency");
        Console.WriteLine("  remove <pkg>          Remove a package dependency");
        Console.WriteLine("  update <pkg>          Update a package dependency");
        Console.WriteLine("  list                  List project dependencies");
        Console.WriteLine("  --version, -v         Display AgentLang version");
        Console.WriteLine("  --help, -h            Show this help documentation\n");
        Console.WriteLine("Flags for 'run':");
        Console.WriteLine("  --yes, -y             Automatically approve operations requiring human approval");
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
        Console.WriteLine($"  agent run main.agent");
        return 0;
    }

    private static async Task<int> HandleRunAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent run <file.agent>");
            Console.ResetColor();
            return 1;
        }

        string filePath = args[1];
        bool autoApprove = args.Any(a => a is "--yes" or "-y");

        if (!File.Exists(filePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: File not found: '{filePath}'");
            Console.ResetColor();
            return 1;
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

        await runtime.ExecuteProgramAsync(program);
        return 0;
    }

    private static int HandleBuild(string[] args)
    {
        if (args.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: Missing file path. Usage: agent build <file.agent>");
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

        if (!errors)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Build succeeded: '{filePath}' passed syntax and semantic verification.");
            Console.ResetColor();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"✗ Build failed: verification encountered errors.");
        Console.ResetColor();
        return 1;
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
