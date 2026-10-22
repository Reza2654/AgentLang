using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentLang.Cli;

public sealed record PackageManifest(
    string Name,
    string Version,
    string Description,
    Dictionary<string, string> Dependencies)
{
    public static PackageManifest Default(string name) => new(
        Name: name,
        Version: "0.1.0",
        Description: "An AgentLang AI-native project",
        Dependencies: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "browser", "^1.0.0" },
            { "filesystem", "^1.0.0" }
        });
}

public sealed record LockedDependency(
    string Version,
    string Integrity,
    string? Resolved = null);

public sealed record PackageLock(
    string Name,
    string Version,
    Dictionary<string, LockedDependency> Dependencies,
    DateTime GeneratedAt)
{
    public static PackageLock FromManifest(PackageManifest manifest)
    {
        var locked = new Dictionary<string, LockedDependency>(StringComparer.Ordinal);
        foreach (var (pkg, ver) in manifest.Dependencies)
        {
            string cleanVer = ver.TrimStart('^', '~', 'v');
            string hashInput = $"{pkg}@{cleanVer}";
            string integrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
            locked[pkg] = new LockedDependency(cleanVer, integrity, $"registry.agentlang.org/{pkg}/{cleanVer}");
        }

        return new PackageLock(manifest.Name, manifest.Version, locked, DateTime.UtcNow);
    }
}

public static class ProjectManager
{
    public const string AgentManifestFileName = "agent.json";
    public const string LegacyManifestFileName = "project.json";
    public const string LockFileName = "agent.lock";

    public static string GetManifestFilePath(string directory)
    {
        string agentJson = Path.Combine(directory, AgentManifestFileName);
        if (File.Exists(agentJson)) return agentJson;
        string projectJson = Path.Combine(directory, LegacyManifestFileName);
        if (File.Exists(projectJson)) return projectJson;
        return agentJson;
    }

    public static void ScaffoldProject(string projectName, string targetDirectory)
    {
        string projectPath = Path.Combine(targetDirectory, projectName);
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "agents"));
        Directory.CreateDirectory(Path.Combine(projectPath, "permissions"));
        Directory.CreateDirectory(Path.Combine(projectPath, "tools"));

        // 1. agent.json manifest & agent.lock
        var manifest = PackageManifest.Default(projectName);
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectPath, AgentManifestFileName), manifestJson);

        var lockfile = PackageLock.FromManifest(manifest);
        string lockJson = JsonSerializer.Serialize(lockfile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectPath, LockFileName), lockJson);

        // 2. permissions/default.permission
        string defaultPermission = """
            permission DefaultPermission {
                allow browser.search
                allow browser.fetch
                ask filesystem.write
                cannot terminal
            }
            """;
        File.WriteAllText(Path.Combine(projectPath, "permissions", "default.permission"), defaultPermission);

        // 3. agents/researcher.agent
        string researcherAgent = """
            agent Researcher (
                model = GPT,
                permission = DefaultPermission,
                tools = [browser, filesystem]
            ) {
                context topic = "Autonomous AI Agent Architecture"

                task research {
                    data = research(topic)
                    answer = think(data)
                    return answer
                }

                print("Research output: " + research.result)
            }
            """;
        File.WriteAllText(Path.Combine(projectPath, "agents", "researcher.agent"), researcherAgent);

        // 4. main.agt
        string mainAgent = """
            permission DefaultPermission {
                allow browser.search
                allow browser.fetch
                ask filesystem.write
                cannot terminal
            }

            agent Researcher (
                model = GPT,
                permission = DefaultPermission,
                tools = [browser, filesystem]
            ) {
                context topic = "Autonomous AI Agent Architecture"

                task research {
                    data = research(topic)
                    answer = think(data)
                    return answer
                }

                print("Research output: " + research.result)
            }

            main {
                print("Starting AgentLang workflow...")
                agent Researcher
                print("Workflow finished successfully!")
            }
            """;
        File.WriteAllText(Path.Combine(projectPath, "main.agt"), mainAgent);

        // 5. README.md
        string readme = $"""
            # {projectName}

            Created with AgentLang v0.6.3.

            ## Run
            ```bash
            agent run main.agt
            ```

            ## Bundle package
            ```bash
            agent pack
            ```
            """;
        File.WriteAllText(Path.Combine(projectPath, "README.md"), readme);
    }

    public static PackageManifest LoadManifest(string directory)
    {
        string path = GetManifestFilePath(directory);
        if (!File.Exists(path))
            return PackageManifest.Default(Path.GetFileName(directory));

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PackageManifest>(json) ?? PackageManifest.Default(Path.GetFileName(directory));
    }

    public static void SaveManifest(string directory, PackageManifest manifest)
    {
        string path = Path.Combine(directory, AgentManifestFileName);
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        // Also update lockfile
        var lockfile = PackageLock.FromManifest(manifest);
        string lockJson = JsonSerializer.Serialize(lockfile, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(directory, LockFileName), lockJson);
    }

    public static string PackProject(string projectPath, string? outputPath = null)
    {
        string fullProjectPath = Path.GetFullPath(projectPath);
        if (!Directory.Exists(fullProjectPath))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {fullProjectPath}");
        }

        var manifest = LoadManifest(fullProjectPath);
        string lockPath = Path.Combine(fullProjectPath, LockFileName);
        if (!File.Exists(lockPath))
        {
            var lockfile = PackageLock.FromManifest(manifest);
            File.WriteAllText(lockPath, JsonSerializer.Serialize(lockfile, new JsonSerializerOptions { WriteIndented = true }));
        }

        string targetZip;
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            targetZip = Path.GetFullPath(outputPath);
            if (!targetZip.EndsWith(".agtpkg", StringComparison.OrdinalIgnoreCase))
                targetZip += ".agtpkg";
        }
        else
        {
            targetZip = Path.Combine(fullProjectPath, $"{manifest.Name}-{manifest.Version}.agtpkg");
        }

        if (File.Exists(targetZip))
            File.Delete(targetZip);

        using (var zip = ZipFile.Open(targetZip, ZipArchiveMode.Create))
        {
            var allFiles = Directory.GetFiles(fullProjectPath, "*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                string rel = Path.GetRelativePath(fullProjectPath, file).Replace('\\', '/');
                if (rel.StartsWith(".git/") || rel.StartsWith("bin/") || rel.StartsWith("obj/") ||
                    rel.StartsWith(".vs/") || rel.EndsWith(".agtpkg", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                zip.CreateEntryFromFile(file, rel, CompressionLevel.Optimal);
            }
        }

        return targetZip;
    }

    public static void UnpackPackage(string packageFile, string targetDir)
    {
        if (!File.Exists(packageFile))
            throw new FileNotFoundException($"Package not found: {packageFile}");

        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(packageFile, targetDir, overwriteFiles: true);
    }

    public static (string DockerfilePath, string DockerignorePath) GenerateDockerFiles(string projectDirectory, string entrypoint = "main.agt")
    {
        string dockerfile = $"""
            # AgentLang Micro-Agent Container
            # Multi-stage ultra-lightweight deployment image
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            WORKDIR /app

            # Copy project files
            COPY . .

            # Runtime runner image
            FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
            WORKDIR /agent

            ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
            ENV AGENTLANG_ENV=production

            COPY . .

            ENTRYPOINT ["dotnet", "run", "--project", "src/AgentLang.Cli", "--", "run", "{entrypoint}"]
            """;

        string dockerignore = """
            .git
            .vs
            bin/
            obj/
            *.agtpkg
            *.log
            """;

        string dockerfilePath = Path.Combine(projectDirectory, "Dockerfile");
        string dockerignorePath = Path.Combine(projectDirectory, ".dockerignore");

        File.WriteAllText(dockerfilePath, dockerfile);
        File.WriteAllText(dockerignorePath, dockerignore);

        return (dockerfilePath, dockerignorePath);
    }
}
