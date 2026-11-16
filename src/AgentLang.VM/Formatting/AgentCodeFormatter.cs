using System.Text;

namespace AgentLang.VM.Formatting;

/// <summary>
/// Canonical source code formatter for AgentLang programs (.agt, .agent),
/// shared across the CLI (agent fmt / format) and the Language Server Protocol (textDocument/formatting).
/// </summary>
public static class AgentCodeFormatter
{
    private const string IndentUnit = "    "; // 4 spaces

    public static string Format(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var rawLines = code.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        int indentLevel = 0;
        bool lastLineWasBlank = false;

        for (int i = 0; i < rawLines.Length; i++)
        {
            string raw = rawLines[i].Trim();

            // Handle blank lines: collapse multiple consecutive blank lines into at most one
            if (string.IsNullOrEmpty(raw))
            {
                if (!lastLineWasBlank && sb.Length > 0)
                {
                    sb.AppendLine();
                    lastLineWasBlank = true;
                }
                continue;
            }

            lastLineWasBlank = false;

            // Check if current line starts with closing brace or bracket
            bool startsWithCloser = raw.StartsWith('}') || raw.StartsWith(']');
            if (startsWithCloser)
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            // Apply indentation
            for (int k = 0; k < indentLevel; k++)
            {
                sb.Append(IndentUnit);
            }

            // Format line content (spacing, commas, etc.)
            string formattedLine = FormatLineTokens(raw);
            sb.AppendLine(formattedLine);

            // Compute net change in indentation for subsequent lines
            int opens = CountCharsOutsideStrings(raw, '{', '[');
            int closes = CountCharsOutsideStrings(raw, '}', ']');

            if (!startsWithCloser && opens > closes)
            {
                indentLevel += (opens - closes);
            }
            else if (startsWithCloser && opens > (closes - 1))
            {
                indentLevel += opens - (closes - 1);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string FormatLineTokens(string line)
    {
        if (line.StartsWith("//") || line.StartsWith("/*"))
        {
            return line;
        }

        var sb = new StringBuilder(line.Length + 8);
        bool inString = false;
        char stringDelimiter = '\0';
        bool escape = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inString)
            {
                sb.Append(c);
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == stringDelimiter)
                {
                    inString = false;
                }
                continue;
            }

            // String start
            if (c is '"' or '\'')
            {
                inString = true;
                stringDelimiter = c;
                escape = false;
                sb.Append(c);
                continue;
            }

            // Comment start: rest of line is comment
            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                // Ensure a space before inline comments if not already present
                if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
                sb.Append(line[i..]);
                break;
            }

            // Normalize comma: always followed by single space, never preceded by space
            if (c == ',')
            {
                while (sb.Length > 0 && sb[^1] == ' ')
                {
                    sb.Length--;
                }
                sb.Append(", ");
                // Skip spaces in input after comma
                while (i + 1 < line.Length && line[i + 1] == ' ')
                {
                    i++;
                }
                continue;
            }

            // Normalize colon in property/config declarations: "model: 'gpt'" -> "model: 'gpt'"
            if (c == ':')
            {
                while (sb.Length > 0 && sb[^1] == ' ')
                {
                    sb.Length--;
                }
                sb.Append(": ");
                while (i + 1 < line.Length && line[i + 1] == ' ')
                {
                    i++;
                }
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().TrimEnd();
    }

    private static int CountCharsOutsideStrings(string s, char c1, char c2)
    {
        int count = 0;
        bool inString = false;
        char quote = '\0';
        bool escape = false;

        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (ch == '\\') escape = true;
                else if (ch == quote) inString = false;
                continue;
            }

            if (ch is '"' or '\'')
            {
                inString = true;
                quote = ch;
                escape = false;
                continue;
            }

            if (ch == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                break; // comment till end of line
            }

            if (ch == c1 || ch == c2)
            {
                count++;
            }
        }

        return count;
    }
}
