using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Runtime.Memory;
using AgentLang.Runtime.Values;
using AgentLang.Transpiler;
using Xunit;

namespace AgentLang.Tests;

public class V062Tests
{
    private static async Task<(AgentLangRuntime Runtime, string Output)> RunCodeAsync(string code)
    {
        var output = new StringWriter();
        var sourceText = new SourceText(code, "test.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var runtime = new AgentLangRuntime(output: output);
        await runtime.ExecuteProgramAsync(program);
        return (runtime, output.ToString());
    }

    [Fact]
    public void VectorStore_Memory_TFIDF_Cosine_Similarity_Retrieval()
    {
        var vectorStore = new VectorStoreMemory(collection: "ai_docs", topK: 2);
        vectorStore.Store("Quantum computing utilizes qubits for exponentially faster calculations.");
        vectorStore.Store("Deep learning and neural networks excel at computer vision and natural language processing.");
        vectorStore.Store("Recipes for traditional Italian sourdough pizza and tomato pasta sauces.");

        var results = vectorStore.Retrieve("neural network language model");
        Assert.NotEmpty(results);
        Assert.Contains("Deep learning", results[0]);
    }

    [Fact]
    public void ConversationBuffer_Sliding_Window_Preserves_Recent_Turns()
    {
        var buffer = new ConversationBufferMemory(window: 2);
        buffer.Add("user", "Hello 1");
        buffer.Add("assistant", "Hi 1");
        buffer.Add("user", "Hello 2");
        buffer.Add("assistant", "Hi 2");
        buffer.Add("user", "Hello 3");
        buffer.Add("assistant", "Hi 3");

        var history = buffer.Retrieve("");
        // Window 2 means 2 user turns + 2 assistant turns = 4 items
        Assert.Equal(4, history.Count);
        Assert.Contains("Hello 2", history[0]);
        Assert.Contains("Hello 3", history[2]);
    }

    [Fact]
    public void EphemeralMemory_Store_And_Retrieve()
    {
        var ephemeral = new EphemeralMemory();
        ephemeral.Set("session_token", "abc-123-xyz");
        ephemeral.Store("Temporary auth trace verified for user admin");

        Assert.Equal("abc-123-xyz", ephemeral.Get("session_token"));
        var found = ephemeral.Retrieve("auth trace");
        Assert.Single(found);
    }

    [Fact]
    public async Task Agent_With_ConversationBuffer_And_ReasonAndReply()
    {
        string code = """
            agent SupportBot {
                model: "mock"
                persona: "Customer Support Specialist"
                memory: ConversationBuffer(window: 5)
            }

            reply1 = SupportBot.reason_and_reply("Can I reset my password?", "User account settings")
            print("REPLY1:", reply1)
            """;

        var (_, output) = await RunCodeAsync(code);
        Assert.Contains("REPLY1:", output);
        Assert.Contains("SupportBot", output);
        Assert.Contains("password", output);
    }

    [Fact]
    public async Task Reactive_Event_Handler_With_Decide_And_Semantic_Condition()
    {
        string code = """
            agent SecurityGuard {
                model: "mock"

                on event(ServerLog log) {
                    decide (log.content contains_suspicious_activity) {
                        reasoning: "Block brute-force intrusion attempt"
                        action {
                            block_ip(log.ip)
                            send_slack_alert(channel: "#sec-ops", message: "Intrusion detected")
                            print("THREAT_CONTAINED")
                        }
                    }
                }
            }

            logEntry = {
                "ip": "192.168.1.100",
                "content": "Multiple failed login brute force attack detected from remote host"
            }

            SecurityGuard.handle(logEntry)
            """;

        var (_, output) = await RunCodeAsync(code);
        Assert.Contains("FIREWALL", output);
        Assert.Contains("192.168.1.100", output);
        Assert.Contains("SLACK ALERT", output);
        Assert.Contains("THREAT_CONTAINED", output);
    }

    [Fact]
    public async Task BuiltIn_EmitReply_And_Contains_Operator()
    {
        string code = """
            text = "Welcome to the agent scripting platform"
            if (text contains "scripting") {
                emit_reply("user_99", "Hello from reactive agent!")
            }
            """;

        var (_, output) = await RunCodeAsync(code);
        Assert.Contains("[REPLY -> user_99]: Hello from reactive agent!", output);
    }

    [Fact]
    public void Transpiler_Python_Generates_LangGraph_CrewAI_Compatible_Code()
    {
        string code = """
            state WorkflowState {
                query: string
                results: list[string]
            }

            agent Analyst {
                model: "claude-3-5-sonnet"
                persona: "Senior Market Analyst"
                memory: ConversationBuffer(window: 5)
            }

            pipeline MarketPipeline {
                input topic: string
                draft = Analyst.achieve(topic)
                return draft
            }

            task = "Analyze AI energy consumption"
            res = MarketPipeline.run(task)
            print(res)
            """;

        var sourceText = new SourceText(code, "test.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        string pythonCode = TranspilerEngine.TranspileToPython(program);

        Assert.Contains("class WorkflowState:", pythonCode);
        Assert.Contains("@dataclass", pythonCode);
        Assert.Contains("class AnalystAgent:", pythonCode);
        Assert.Contains("async def MarketPipeline(", pythonCode);
        Assert.Contains("async def main():", pythonCode);
        Assert.Contains("Analyst = AnalystAgent()", pythonCode);
    }

    [Fact]
    public void Transpiler_TypeScript_Generates_Vercel_AI_SDK_Compatible_Code()
    {
        string code = """
            state AgentState {
                step: int
                logs: list[string]
            }

            agent Assistant {
                model: "gpt-4o"
                role: "Helpful Assistant"
            }

            workflow RunAssistant(req: string): string {
                ans = Assistant.achieve(req)
                return ans
            }

            res = RunAssistant("Hello AI")
            print(res)
            """;

        var sourceText = new SourceText(code, "test.agt");
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        string tsCode = TranspilerEngine.TranspileToTypeScript(program);

        Assert.Contains("export interface AgentState {", tsCode);
        Assert.Contains("export class AssistantAgent {", tsCode);
        Assert.Contains("export async function RunAssistant(", tsCode);
        Assert.Contains("export async function main() {", tsCode);
        Assert.Contains("export const Assistant = new AssistantAgent();", tsCode);
    }
}
