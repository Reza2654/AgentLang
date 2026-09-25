namespace AgentLang.Cli;

public static class EnvLoader
{
    public static void Load(string? directory = null)
    {
        string dir = directory ?? Directory.GetCurrentDirectory();
        string envPath = Path.Combine(dir, ".env");

        if (!File.Exists(envPath))
        {
            // Check parent directory
            var parent = Directory.GetParent(dir);
            if (parent != null)
            {
                string parentEnv = Path.Combine(parent.FullName, ".env");
                if (File.Exists(parentEnv))
                    envPath = parentEnv;
            }
        }

        if (!File.Exists(envPath))
            return;

        try
        {
            var lines = File.ReadAllLines(envPath);
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("//"))
                    continue;

                int eqIdx = line.IndexOf('=');
                if (eqIdx <= 0)
                    continue;

                string key = line[..eqIdx].Trim();
                string val = line[(eqIdx + 1)..].Trim();

                // Strip surrounding quotes
                if ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\'')))
                {
                    if (val.Length >= 2)
                        val = val[1..^1];
                }

                // Only set if not already present in environment
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                {
                    Environment.SetEnvironmentVariable(key, val);
                }
            }
        }
        catch
        {
            // Silently ignore .env read issues to prevent crashing
        }
    }
}
