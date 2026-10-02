using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using Xunit;

namespace AgentLang.Tests;

public class V060Tests
{
    private static (Parser.Parser parser, ProgramNode program, SemanticAnalyzer semantic) ParseAndAnalyze(string code)
    {
        var source = new SourceText(code);
        var parser = new Parser.Parser(source);
        var program = parser.ParseProgram();
        var semantic = new SemanticAnalyzer();
        semantic.Analyze(program);
        return (parser, program, semantic);
    }

    private static async Task<string> RunCodeAsync(string code)
    {
        var (parser, program, semantic) = ParseAndAnalyze(code);
        Assert.False(parser.Diagnostics.HasErrors, string.Join("; ", parser.Diagnostics.Errors));
        Assert.False(semantic.Diagnostics.HasErrors, string.Join("; ", semantic.Diagnostics.Errors));

        var sw = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(sw);
            var runtime = new AgentLangRuntime();
            await runtime.ExecuteProgramAsync(program);
            return sw.ToString().Trim();
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Lexer_Tokenizes_V060_Keywords_And_Literals()
    {
        string code = "goal pipeline workflow state decide reasoning action loop budget guardrails strategy persona rules case default max_retries on init from break continue -> 45s 100ms 2h";
        var lexer = new Lexer.Lexer(new SourceText(code));
        var tokens = lexer.TokenizeAll();

        Assert.Equal(TokenType.Goal, tokens[0].Type);
        Assert.Equal(TokenType.Pipeline, tokens[1].Type);
        Assert.Equal(TokenType.Workflow, tokens[2].Type);
        Assert.Equal(TokenType.State, tokens[3].Type);
        Assert.Equal(TokenType.Decide, tokens[4].Type);
        Assert.Equal(TokenType.Reasoning, tokens[5].Type);
        Assert.Equal(TokenType.Action, tokens[6].Type);
        Assert.Equal(TokenType.Loop, tokens[7].Type);
        Assert.Equal(TokenType.Budget, tokens[8].Type);
        Assert.Equal(TokenType.Guardrails, tokens[9].Type);
        Assert.Equal(TokenType.Strategy, tokens[10].Type);
        Assert.Equal(TokenType.Persona, tokens[11].Type);
        Assert.Equal(TokenType.Rules, tokens[12].Type);
        Assert.Equal(TokenType.Case, tokens[13].Type);
        Assert.Equal(TokenType.Default, tokens[14].Type);
        Assert.Equal(TokenType.MaxRetries, tokens[15].Type);
        Assert.Equal(TokenType.On, tokens[16].Type);
        Assert.Equal(TokenType.Init, tokens[17].Type);
        Assert.Equal(TokenType.From, tokens[18].Type);
        Assert.Equal(TokenType.Break, tokens[19].Type);
        Assert.Equal(TokenType.Continue, tokens[20].Type);
        Assert.Equal(TokenType.Arrow, tokens[21].Type);

        // Durations
        Assert.Equal(TokenType.NumberLiteral, tokens[22].Type);
        Assert.Equal(45.0, tokens[22].Value);
        Assert.Equal(TokenType.NumberLiteral, tokens[23].Type);
        Assert.Equal(0.1, (double)tokens[23].Value!, 3);
        Assert.Equal(TokenType.NumberLiteral, tokens[24].Type);
        Assert.Equal(7200.0, tokens[24].Value);
    }

    [Fact]
    public void Lexer_Tokenizes_Multiline_And_FStrings()
    {
        string code = "\"\"\"Line 1\nLine 2\"\"\" f\"Total is {count} items\"";
        var lexer = new Lexer.Lexer(new SourceText(code));
        var tokens = lexer.TokenizeAll();

        Assert.Equal(TokenType.StringLiteral, tokens[0].Type);
        Assert.Equal("Line 1\nLine 2", tokens[0].Value);

        Assert.Equal(TokenType.StringLiteral, tokens[1].Type);
        Assert.Equal("Total is {count} items", tokens[1].Value);
    }

    [Fact]
    public async Task Runtime_Executes_TopLevel_ResearchAssistant_Goal_Achieve()
    {
        string code = """
        agent ResearchAssistant {
            model: "claude-3-opus"
            persona: "تحلیل‌گر دقیق و خلاصه"
            tools: [web_search, summarize_document]
            memory: ConversationBuffer(window: 5)
            strategy: ReAct(max_steps: 4)
        }

        goal task1 = "بررسی آخرین مقالات هوش مصنوعی در سال ۲۰۲۶"

        response = ResearchAssistant.achieve(task1)
        print(response.output)
        """;

        string output = await RunCodeAsync(code);
        Assert.Contains("بررسی آخرین مقالات هوش مصنوعی در سال ۲۰۲۶", output);
    }

    [Fact]
    public async Task Runtime_Executes_ContentPipeline_With_LoopUntil_MaxRetries()
    {
        string code = """
        agent Writer {
            model: "gpt-4o"
            goal: "نگارش متن بر اساس اطلاعات ورودی"
        }

        agent Critic {
            model: "gpt-4o"
            rules: [
                "نباید غلط املایی وجود داشته باشد",
                "لحن باید رسمی باشد"
            ]
        }

        pipeline ContentPipeline {
            input topic: string
            
            draft = Writer.draft(topic)
            
            loop until Critic.approves(draft) max_retries 3 {
                feedback = Critic.review(draft)
                draft = Writer.revise(draft, with: feedback)
            }
            
            return draft
        }

        result = ContentPipeline.run("آینده انرژی‌های تجدیدپذیر")
        print(result.output)
        """;

        string output = await RunCodeAsync(code);
        Assert.Contains("آینده انرژی‌های تجدیدپذیر", output);
    }

    [Fact]
    public async Task Runtime_Executes_State_And_Workflow_With_LoopAttempts_And_Break()
    {
        string code = """
        state ProjectState {
            spec: string = ""
            code: string = ""
            test_results: list[string] = []
            status: enum { PLANNING, CODING, REVIEW, DONE } = PLANNING
        }

        agent TechLead {
            model: "gpt-4o"
            role: "شکستن نیازمندی‌ها به توابع مشخص پایتون"
        }

        agent Developer {
            model: "claude-3-5-sonnet"
            role: "نوشتن کدهای تمیز طبق مشخصات ارائه شده"
        }

        agent QAEngineer {
            model: "gpt-4o"
            role: "تست کدها و گزارش باگ‌های مرزی"
        }

        workflow BuildFeature(user_story: string) -> ProjectState {
            init state = ProjectState()

            state.spec = TechLead.execute("طراحی نیازمندی برای: " + user_story)
            state.status = CODING

            loop attempts from 1 to 3 {
                state.code = Developer.execute(
                    spec: state.spec,
                    feedback: state.test_results
                )
                
                test_run = QAEngineer.run_tests(code: state.code)
                state.test_results = test_run.errors

                if (test_run.passed) {
                    state.status = DONE
                    break
                }
            }

            return state
        }

        final_state = BuildFeature("یک ماژول اعتبارسنجی شماره کارت شتاب")
        print("Final Status: " + final_state.status)
        """;

        string output = await RunCodeAsync(code);
        Assert.Contains("Final Status: DONE", output);
    }

    [Fact]
    public async Task Runtime_Executes_DecideStatement_With_Reasoning_And_Action()
    {
        string code = """
        log_suspicious = true
        log_ip = "192.168.1.100"
        action_msg = ""

        decide (log_suspicious) {
            reasoning: "بررسی الگوهای نفوذ یا حملات Brute Force"
            action {
                action_msg = f"Blocked IP {log_ip} due to suspicious activity"
            }
        }

        print(action_msg)
        """;

        string output = await RunCodeAsync(code);
        Assert.Equal("Blocked IP 192.168.1.100 due to suspicious activity", output);
    }

    [Fact]
    public async Task Runtime_Executes_DecideCases_And_Default()
    {
        string code = """
        sentiment = "very_angry"
        decision = ""

        decide {
            case sentiment == "very_angry":
                decision = "escalate_to_human"
            case sentiment == "happy":
                decision = "auto_reply"
            default:
                decision = "standard_queue"
        }

        print(decision)
        """;

        string output = await RunCodeAsync(code);
        Assert.Equal("escalate_to_human", output);
    }

    [Fact]
    public async Task Runtime_Executes_BudgetStatement()
    {
        string code = """
        total = 0
        budget max_tokens: 1500 max_cost: 0.05 {
            x = 40
            y = 60
            total = x + y
        }
        print(total)
        """;

        string output = await RunCodeAsync(code);
        Assert.Equal("100", output);
    }

    [Fact]
    public async Task Runtime_Executes_GuardrailsDeclaration_And_Typed_Catch()
    {
        string code = """
        agent FinancialAnalyst {
            model: "claude-3-5-sonnet"
            guardrails {
                forbid: ["سیگنال خرید قطعی", "تضمین سود"]
            }
        }

        status = "safe"
        try {
            print("Analyst running...")
        } catch (GuardrailViolationError err) {
            status = "guardrail_violated"
        } catch (err) {
            status = "generic_error"
        }
        print(status)
        """;

        string output = await RunCodeAsync(code);
        Assert.Contains("Analyst running...", output);
        Assert.Contains("safe", output);
    }

    [Fact]
    public async Task Runtime_Executes_TopLevel_Scripting_Without_Main()
    {
        string code = """
        val a = 25
        val b = 4
        print(a * b)
        """;

        string output = await RunCodeAsync(code);
        Assert.Equal("100", output);
    }
}
