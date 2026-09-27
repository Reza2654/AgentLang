using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLang.Models;
using AgentLang.Runtime.Values;
using AgentLang.Security;
using AgentLang.Tools;

namespace AgentLang.Runtime.Agents;

public sealed class AgentReActEngine
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ModelRegistry _modelRegistry;
    private readonly SecurityEngine _securityEngine;
    private readonly TextWriter? _output;
    private readonly EventBus? _eventBus;

    public AgentReActEngine(
        ToolRegistry toolRegistry,
        ModelRegistry modelRegistry,
        SecurityEngine? securityEngine = null,
        TextWriter? output = null,
        EventBus? eventBus = null)
    {
        _toolRegistry = toolRegistry;
        _modelRegistry = modelRegistry;
        _securityEngine = securityEngine ?? new SecurityEngine();
        _output = output;
        _eventBus = eventBus;
    }

    public async Task<AgentReActResult> SolveAsync(
        AgentValue agent,
        string goal,
        RuntimeScope scope,
        CancellationToken ct = default)
    {
        return await RunReActLoopAsync(agent, goal, isChat: false, scope, ct);
    }

    public async Task<string> ChatAsync(
        AgentValue agent,
        string userMessage,
        RuntimeScope scope,
        CancellationToken ct = default)
    {
        var result = await RunReActLoopAsync(agent, userMessage, isChat: true, scope, ct);
        return result.FinalAnswer ?? result.Error ?? "پاسخی از ربات دریافت نشد.";
    }

    public async Task<AgentReActResult> RunReActLoopAsync(
        AgentValue agent,
        string input,
        bool isChat,
        RuntimeScope scope,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var steps = new List<ReActStep>();

        if (isChat)
        {
            agent.History.Add(new ChatMessage("user", input));
        }

        string role = agent.Role ?? agent.Persona ?? "Autonomous AI Specialist";
        string instructions = agent.Instructions ?? agent.Goal ?? "Analyze the user's request, use available tools autonomously, and provide a clear, accurate solution.";
        int maxSteps = agent.MaxSteps > 0 ? agent.MaxSteps : 10;

        var availableTools = GetAvailableTools(agent);
        string toolDescriptions = BuildToolsDescription(availableTools);

        string systemPrompt = $$"""
You are an autonomous AI agent named '{{agent.Name}}'.
Role: {{role}}
Instructions: {{instructions}}

Available Tools:
{{toolDescriptions}}

Reason step by step to solve the request.
Strictly adhere to this format:
Thought: <reasoning about what step to take next>
Action: <tool_name>({"key": "value"})
Observation: <result of the tool call>
... (repeat Thought/Action/Observation as needed)
Thought: I have sufficient information to answer.
Final Answer: <your final response>
""";

        string primaryModel = agent.Model ?? "mock";
        var provider = _modelRegistry.Resolve(primaryModel);

        var conversationTrace = new List<string>();

        // Include chat history context if in chat mode
        if (isChat && agent.History.Count > 1)
        {
            conversationTrace.Add("--- Previous Conversation History ---");
            foreach (var msg in agent.History.Take(agent.History.Count - 1))
            {
                conversationTrace.Add($"{msg.Role}: {msg.Content}");
            }
            conversationTrace.Add("-------------------------------------");
        }

        conversationTrace.Add($"User Request: {input}");

        for (int stepIndex = 1; stepIndex <= maxSteps; stepIndex++)
        {
            ct.ThrowIfCancellationRequested();

            string currentContext = string.Join("\n", conversationTrace);

            string stepResponse;
            if (provider is MockModelProvider || primaryModel.Equals("mock", StringComparison.OrdinalIgnoreCase))
            {
                stepResponse = await GenerateHeuristicReActStepAsync(agent, input, stepIndex, steps, availableTools, ct);
            }
            else
            {
                var req = new ModelRequest(
                    ModelName: primaryModel,
                    Prompt: currentContext,
                    SystemInstruction: systemPrompt,
                    Temperature: agent.Temperature ?? 0.2);

                var res = await provider.GenerateAsync(req, ct);
                if (!res.Success)
                {
                    return new AgentReActResult(false, null, steps, sw.Elapsed, $"Model generation error: {res.ErrorMessage}");
                }
                stepResponse = res.Content;
            }

            conversationTrace.Add(stepResponse);

            // 1. Check for Final Answer
            int finalAnswerIdx = stepResponse.IndexOf("Final Answer:", StringComparison.OrdinalIgnoreCase);
            if (finalAnswerIdx >= 0)
            {
                string finalAnswer = stepResponse.Substring(finalAnswerIdx + "Final Answer:".Length).Trim();
                string thought = ExtractThought(stepResponse, finalAnswerIdx);

                steps.Add(new ReActStep(stepIndex, thought, null, null, null));

                if (isChat)
                {
                    agent.History.Add(new ChatMessage("assistant", finalAnswer));
                }

                if (agent.MemoryEnabled)
                {
                    agent.Memory.Add($"{agent.Name} solved: {input} -> {finalAnswer}");
                }

                if (_eventBus != null)
                {
                    await _eventBus.PublishAsync($"{agent.Name}.finished", finalAnswer);
                }

                sw.Stop();
                return new AgentReActResult(true, finalAnswer, steps, sw.Elapsed);
            }

            // 2. Parse Action and execute tool
            var (thoughtText, toolName, toolArgs) = ParseAction(stepResponse);
            if (string.IsNullOrWhiteSpace(toolName))
            {
                // If model didn't provide a structured action, treat entire text as final answer
                string directAnswer = stepResponse.Trim();
                steps.Add(new ReActStep(stepIndex, "Direct response produced without tool action.", null, null, null));
                if (isChat) agent.History.Add(new ChatMessage("assistant", directAnswer));
                sw.Stop();
                return new AgentReActResult(true, directAnswer, steps, sw.Elapsed);
            }

            // Execute the tool
            var toolResult = await _toolRegistry.InvokeAsync(
                agent.Name,
                agent.PermissionPolicy,
                toolName,
                toolArgs,
                ct);

            string observation = toolResult.Success
                ? FormatToolOutput(toolResult.Output)
                : $"Error: {toolResult.Error}";

            steps.Add(new ReActStep(stepIndex, thoughtText, toolName, toolArgs, observation));

            if (_eventBus != null)
            {
                await _eventBus.PublishAsync($"{agent.Name}.step", new
                {
                    Step = stepIndex,
                    Thought = thoughtText,
                    Tool = toolName,
                    Observation = observation
                });
            }

            conversationTrace.Add($"Observation: {observation}");
        }

        // Max steps reached
        string fallbackSummary = $"حلقه استدلال ربات پس از {maxSteps} مرحله به پایان رسید. آخرین نتیجه مشاهده شده: {steps.LastOrDefault()?.Observation ?? "موردی ثبت نشد."}";
        if (isChat) agent.History.Add(new ChatMessage("assistant", fallbackSummary));
        sw.Stop();
        return new AgentReActResult(true, fallbackSummary, steps, sw.Elapsed);
    }

    private List<ITool> GetAvailableTools(AgentValue agent)
    {
        if (agent.BoundTools.Count == 0)
        {
            return _toolRegistry.AllTools.ToList();
        }

        var list = new List<ITool>();
        foreach (var tool in _toolRegistry.AllTools)
        {
            if (agent.BoundTools.Any(b => b.Equals(tool.Name, StringComparison.OrdinalIgnoreCase) ||
                                         tool.SupportedCapabilities.Any(c => c.Equals(b, StringComparison.OrdinalIgnoreCase))))
            {
                list.Add(tool);
            }
        }
        return list.Count > 0 ? list : _toolRegistry.AllTools.ToList();
    }

    private static string BuildToolsDescription(List<ITool> tools)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in tools)
        {
            foreach (var cap in t.SupportedCapabilities)
            {
                sb.AppendLine($"- {cap}: Provides capability for '{t.Name}'");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static string ExtractThought(string response, int finalAnswerIdx)
    {
        int thoughtIdx = response.IndexOf("Thought:", StringComparison.OrdinalIgnoreCase);
        if (thoughtIdx >= 0 && thoughtIdx < finalAnswerIdx)
        {
            int start = thoughtIdx + "Thought:".Length;
            return response.Substring(start, finalAnswerIdx - start).Trim();
        }
        return "Concluded with final answer.";
    }

    private static (string Thought, string? ToolName, Dictionary<string, object?> Arguments) ParseAction(string text)
    {
        string thought = "";
        int thoughtIdx = text.IndexOf("Thought:", StringComparison.OrdinalIgnoreCase);
        int actionIdx = text.IndexOf("Action:", StringComparison.OrdinalIgnoreCase);

        if (thoughtIdx >= 0)
        {
            int end = actionIdx > thoughtIdx ? actionIdx : text.Length;
            thought = text.Substring(thoughtIdx + "Thought:".Length, end - (thoughtIdx + "Thought:".Length)).Trim();
        }

        if (actionIdx < 0)
        {
            return (thought, null, []);
        }

        string actionPart = text.Substring(actionIdx + "Action:".Length).Trim();
        int newlineIdx = actionPart.IndexOf('\n');
        if (newlineIdx > 0)
        {
            actionPart = actionPart.Substring(0, newlineIdx).Trim();
        }

        // Match tool_name(...) or tool_name({...})
        var match = Regex.Match(actionPart, @"^([a-zA-Z0-9_\.]+)\s*(\(.*?\)|{.*?})?$");
        if (!match.Success)
        {
            string simpleTool = actionPart.Trim();
            return (thought, simpleTool, []);
        }

        string toolName = match.Groups[1].Value.Trim();
        string rawArgs = match.Groups[2].Value.Trim();
        var argsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(rawArgs))
        {
            if (rawArgs.StartsWith("(") && rawArgs.EndsWith(")"))
            {
                rawArgs = rawArgs.Substring(1, rawArgs.Length - 2).Trim();
            }

            if (rawArgs.StartsWith("{") && rawArgs.EndsWith("}"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawArgs);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        argsDict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }
                catch
                {
                    // Fallback to key-value regex
                    ExtractKeyValueArgs(rawArgs, argsDict);
                }
            }
            else
            {
                ExtractKeyValueArgs(rawArgs, argsDict);
            }
        }

        return (thought, toolName, argsDict);
    }

    private static void ExtractKeyValueArgs(string raw, Dictionary<string, object?> dict)
    {
        var kvMatches = Regex.Matches(raw, @"([a-zA-Z0-9_]+)\s*[:=]\s*(?:""([^""]*)""|'([^']*)'|([^,]+))");
        if (kvMatches.Count > 0)
        {
            foreach (Match m in kvMatches)
            {
                string key = m.Groups[1].Value;
                string val = !string.IsNullOrEmpty(m.Groups[2].Value) ? m.Groups[2].Value :
                             !string.IsNullOrEmpty(m.Groups[3].Value) ? m.Groups[3].Value :
                             m.Groups[4].Value.Trim();
                dict[key] = val;
            }
        }
        else if (!string.IsNullOrWhiteSpace(raw))
        {
            string clean = raw.Trim('"', '\'');
            dict["arg0"] = clean;
            dict["path"] = clean;
            dict["query"] = clean;
            dict["expr"] = clean;
            dict["command"] = clean;
        }
    }

    private static string FormatToolOutput(object? output)
    {
        if (output == null) return "(empty result)";
        if (output is string s) return s;
        if (output is IEnumerable<string> lines) return string.Join(", ", lines);
        if (output is System.Collections.IEnumerable list)
        {
            var items = new List<string>();
            foreach (var item in list) items.Add(item?.ToString() ?? "");
            return string.Join(", ", items);
        }
        return output.ToString() ?? "(empty result)";
    }

    private async Task<string> GenerateHeuristicReActStepAsync(
        AgentValue agent,
        string input,
        int stepIndex,
        List<ReActStep> pastSteps,
        List<ITool> tools,
        CancellationToken ct)
    {
        string p = input.ToLowerInvariant();

        // 1. Filesystem analysis
        if (p.Contains("file") || p.Contains("folder") || p.Contains("dir") || p.Contains("پوشه") ||
            p.Contains("فایل") || p.Contains("اسکن") || p.Contains("کد") || p.Contains("scan") || p.Contains("read") || p.Contains("check"))
        {
            if (stepIndex == 1)
            {
                return "Thought: برای تحلیل دقیق، ابتدا باید فایل‌های پوشه را بررسی کنم تا ساختار پروژه مشخص شود.\nAction: filesystem.list({\"path\": \".\"})";
            }
            if (stepIndex == 2)
            {
                // Check if user specified a file
                var fileMatch = Regex.Match(input, @"([a-zA-Z0-9_\-\./\\]+\.[a-zA-Z0-9]+)");
                string targetFile = fileMatch.Success ? fileMatch.Groups[1].Value : "README.md";

                return $"Thought: فهرست فایل‌ها را مشاهده کردم. حال فایل '{targetFile}' را می‌خوانم تا محتوای آن بررسی شود.\nAction: filesystem.read({{\"path\": \"{targetFile}\"}})";
            }

            var lastObs = pastSteps.LastOrDefault()?.Observation ?? "";
            return $"Thought: فایل با موفقیت بررسی و تحلیل شد.\nFinal Answer: بررسی فایل‌ها با موفقیت انجام شد. خلاصه محتوا و وضعیت: {Truncate(lastObs, 200)}";
        }

        // 2. Calculator / Math
        if (Regex.IsMatch(input, @"\d+\s*[\+\-\*/]\s*\d+") || p.Contains("calculate") || p.Contains("محاسبه") || p.Contains("حاصل"))
        {
            if (stepIndex == 1)
            {
                var mathMatch = Regex.Match(input, @"(\d+\s*[\+\-\*/]\s*\d+)");
                string expr = mathMatch.Success ? mathMatch.Groups[1].Value : "2 + 2";
                return $"Thought: برای پاسخ دقیق ریاضی باید از ابزار ماشین حساب استفاده کنم.\nAction: calculator.eval({{\"expr\": \"{expr}\"}})";
            }

            var calcObs = pastSteps.LastOrDefault()?.Observation ?? "0";
            return $"Thought: محاسبه با موفقیت انجام شد.\nFinal Answer: حاصل محاسبه برابر است با {calcObs}.";
        }

        // 3. Search / Knowledge
        if (p.Contains("search") || p.Contains("سرچ") || p.Contains("جستجو") || p.Contains("پیدا کن"))
        {
            if (stepIndex == 1)
            {
                string query = input.Replace("search", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace("سرچ کن", "", StringComparison.OrdinalIgnoreCase)
                                    .Replace("جستجو کن", "", StringComparison.OrdinalIgnoreCase)
                                    .Trim();
                if (string.IsNullOrWhiteSpace(query)) query = input;
                return $"Thought: برای پاسخ به این سوال نیاز به جستجوی وب دارم.\nAction: browser.search({{\"query\": \"{query}\"}})";
            }

            var searchObs = pastSteps.LastOrDefault()?.Observation ?? "";
            return $"Thought: نتایج جستجو دریافت شد.\nFinal Answer: بر اساس نتایج جستجو: {Truncate(searchObs, 250)}";
        }

        // 4. Conversational / Direct Response
        return $"Thought: کاربر پیامی ارسال کرده است و با توجه به نقش '{agent.Role ?? agent.Name}' پاسخ می‌دهم.\nFinal Answer: سلام! من ربات '{agent.Name}' هستم با نقش '{agent.Role ?? "دستیار هوشمند"}'. دستورالعمل من این است: {agent.Instructions ?? "پاسخگویی و حل هوشمندانه مسائل"}. چطور می‌توانم به شما کمک کنم؟";
    }

    private static string Truncate(string str, int maxLen)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLen ? str : str.Substring(0, maxLen) + "...";
    }
}
