using System.Text;

namespace AgentLang.Cli;

public static class FormatCommand
{
    public static int Run(string[] args)
    {
        bool write = args.Any(a => a is "--write" or "-w");
        bool check = args.Any(a => a is "--check" or "-c");

        var pathArgs = args.Skip(1).Where(a => !a.StartsWith('-')).ToList();
        string target = pathArgs.Count > 0 ? pathArgs[0] : ".";

        var files = new List<string>();
        if (File.Exists(target))
            files.Add(target);
        else if (Directory.Exists(target))
        {
            files.AddRange(Directory.GetFiles(target, "*.agt", SearchOption.AllDirectories));
            files.AddRange(Directory.GetFiles(target, "*.agent", SearchOption.AllDirectories));
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: Target '{target}' does not exist.");
            Console.ResetColor();
            return 1;
        }

        int unformattedCount = 0;
        foreach (var file in files)
        {
            string original = File.ReadAllText(file);
            string formatted = FormatCode(original);

            bool needsFormat = !string.Equals(original.Replace("\r\n", "\n"), formatted.Replace("\r\n", "\n"));
            string rel = Path.GetRelativePath(Directory.GetCurrentDirectory(), file);

            if (needsFormat)
            {
                unformattedCount++;
                if (write)
                {
                    File.WriteAllText(file, formatted);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Formatted: {rel}");
                    Console.ResetColor();
                }
                else if (check)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Needs formatting: {rel}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine(formatted);
                }
            }
            else if (!check && write)
            {
                Console.WriteLine($"Already formatted: {rel}");
            }
        }

        if (check)
        {
            if (unformattedCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n{unformattedCount} file(s) need formatting. Run 'agent format --write' to reformat.");
                Console.ResetColor();
                return 1;
            }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("All files are properly formatted.");
            Console.ResetColor();
            return 0;
        }

        return 0;
    }

    public static string FormatCode(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        int indent = 0;
        const string indentStr = "    ";

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }

            var (openCount, closeCount, startsWithClose) = AnalyzeLineBraces(line);

            if (startsWithClose)
            {
                indent = Math.Max(0, indent - 1);
            }

            for (int k = 0; k < indent; k++)
            {
                sb.Append(indentStr);
            }
            sb.AppendLine(line);

            if (!startsWithClose && openCount > closeCount)
            {
                indent += (openCount - closeCount);
            }
            else if (startsWithClose && openCount > (closeCount - 1))
            {
                indent += openCount - (closeCount - 1);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static (int open, int close, bool startsWithClose) AnalyzeLineBraces(string line)
    {
        int open = 0;
        int close = 0;
        bool inString = false;
        bool escape = false;
        bool firstBraceSeen = false;
        bool startsWithClose = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }
                continue;
            }

            // Check for comment //
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                if (!firstBraceSeen)
                {
                    firstBraceSeen = true;
                }
                open++;
            }
            else if (c == '}')
            {
                if (!firstBraceSeen)
                {
                    firstBraceSeen = true;
                    startsWithClose = true;
                }
                close++;
            }
        }

        return (open, close, startsWithClose);
    }
}
