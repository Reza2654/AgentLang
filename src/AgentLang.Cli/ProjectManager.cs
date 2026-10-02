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

public static class ProjectManager
{
    private const string ManifestFileName = "project.json";

    public static void ScaffoldProject(string projectName, string targetDirectory)
    {
        string projectPath = Path.Combine(targetDirectory, projectName);
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(Path.Combine(projectPath, "agents"));
        Directory.CreateDirectory(Path.Combine(projectPath, "permissions"));
        Directory.CreateDirectory(Path.Combine(projectPath, "tools"));

        // 1. project.json manifest
        var manifest = PackageManifest.Default(projectName);
        string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(projectPath, ManifestFileName), manifestJson);

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

            Created with AgentLang v0.6.0.

            ## Run
            ```bash
            agent run main.agt
            ```
            """;
        File.WriteAllText(Path.Combine(projectPath, "README.md"), readme);
    }

    public static PackageManifest LoadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path))
            return PackageManifest.Default(Path.GetFileName(directory));

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PackageManifest>(json) ?? PackageManifest.Default(Path.GetFileName(directory));
    }

    public static void SaveManifest(string directory, PackageManifest manifest)
    {
        string path = Path.Combine(directory, ManifestFileName);
        string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
