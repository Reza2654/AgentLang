using AgentLang.AST;
using AgentLang.Lexer;
using AgentLang.Parser;
using AgentLang.Runtime;
using AgentLang.Semantic;
using Xunit;

namespace AgentLang.Tests;

public class V061Tests
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
    public async Task Guardrail_Forbid_Violation_Caught_And_Exposes_Details()
    {
        string code = """
            agent TestTrader {
                model: "mock"
                guardrails {
                    forbid: ["سیگنال خرید قطعی", "تضمین سود"]
                }
            }

            try {
                res = TestTrader.achieve("سیگنال خرید قطعی بده")
                print("SUCCESS")
            } catch (GuardrailViolationError err) {
                print("VIOLATION:", err.details)
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("VIOLATION:", output);
        Assert.Contains("سیگنال خرید قطعی", output);
        Assert.DoesNotContain("SUCCESS", output);
    }

    [Fact]
    public async Task Guardrail_RequireFormat_Passes_When_Format_Valid()
    {
        string code = """
            agent Analyst {
                model: "mock"
                guardrails {
                    require_format: Schema(
                        summary: string,
                        risks: list[string]
                    )
                }
            }

            try {
                rep = Analyst.achieve("بررسی صورت‌های مالی سه ماهه اپل")
                print("STATUS:", rep.status)
                print("SUMMARY:", rep.summary)
            } catch (GuardrailViolationError err) {
                print("FAIL:", err.details)
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("STATUS: DONE", output);
        Assert.Contains("SUMMARY:", output);
        Assert.DoesNotContain("FAIL:", output);
    }

    [Fact]
    public async Task Budget_Exceeded_Throws_And_Caught_By_BudgetExceededError()
    {
        string code = """
            agent HeavyWorker {
                model: "mock"
            }

            try {
                budget max_tokens: 50 {
                    HeavyWorker.achieve("یک تسک سنگین تحلیلی را انجام بده")
                }
                print("BUDGET_OK")
            } catch (BudgetExceededError err) {
                print("BUDGET_LIMIT_REACHED:", err.message)
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("BUDGET_LIMIT_REACHED:", output);
        Assert.Contains("Token budget exceeded", output);
        Assert.DoesNotContain("BUDGET_OK", output);
    }

    [Fact]
    public async Task Budget_Within_Limit_Succeeds()
    {
        string code = """
            agent SafeWorker {
                model: "mock"
            }

            budget max_tokens: 5000 max_cost: 1.0 {
                SafeWorker.achieve("خلاصه کوتاه")
            }
            print("BUDGET_PASSED")
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("BUDGET_PASSED", output);
    }

    [Fact]
    public async Task Timeout_Triggered_And_Caught_By_TimeoutError()
    {
        string code = """
            agent SlowAgent {
                model: "mock"
            }

            try {
                SlowAgent.achieve(goal: "تسک زمان‌بر", timeout: 10ms)
                print("COMPLETED_IN_TIME")
            } catch (TimeoutError) {
                print("TIMEOUT_CAUGHT")
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("TIMEOUT_CAUGHT", output);
        Assert.DoesNotContain("COMPLETED_IN_TIME", output);
    }

    [Fact]
    public async Task Human_In_The_Loop_Transfer_To_Human_Executes()
    {
        string code = """
            customerId = "USR-9988"
            transfer_to_human(
                customer_id: customerId,
                reason: "نیاز به بررسی مدارک توسط کارشناس انسانی"
            )
            print("ESCALATION_DONE")
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("[HUMAN ESCALATION]", output);
        Assert.Contains("نیاز به بررسی مدارک توسط کارشناس انسانی", output);
        Assert.Contains("ESCALATION_DONE", output);
    }

    [Fact]
    public async Task User_Example_4_Full_Script_Success_Flow()
    {
        string code = """
            agent FinancialAnalyst {
                model: "claude-3-5-sonnet"
                temperature: 0.1

                guardrails {
                    forbid: ["سیگنال خرید قطعی", "تضمین سود"]
                    require_format: Schema(
                        summary: string,
                        risks: list[string],
                        financial_metrics: dict
                    )
                }
            }

            task analyze_apple = "صورت‌های مالی سه ماهه اخیر اپل را بررسی کن و ریسک‌های اصلی را استخراج کن."

            try {
                report = FinancialAnalyst.achieve(
                    goal: analyze_apple,
                    timeout: 45s
                )
                print("گزارش تحلیل:")
                print(report.summary)
                print("ریسک‌ها:", report.risks)
            } catch (GuardrailViolationError err) {
                print("خطا در قوانین اخلاقی/تولید متن:", err.details)
            } catch (TimeoutError) {
                print("تحلیل بیش از حد زمان‌بر شد.")
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("گزارش تحلیل:", output);
        Assert.Contains("ریسک‌ها:", output);
        Assert.DoesNotContain("خطا در قوانین اخلاقی/تولید متن:", output);
        Assert.DoesNotContain("تحلیل بیش از حد زمان‌بر شد.", output);
    }

    [Fact]
    public async Task User_Example_4_Guardrail_Violation_Branch()
    {
        string code = """
            agent FinancialAnalyst {
                model: "claude-3-5-sonnet"
                temperature: 0.1

                guardrails {
                    forbid: ["سیگنال خرید قطعی", "تضمین سود"]
                    require_format: Schema(
                        summary: string,
                        risks: list[string],
                        financial_metrics: dict
                    )
                }
            }

            task bad_task = "به من تضمین سود در خرید سهام بده"

            try {
                report = FinancialAnalyst.achieve(
                    goal: bad_task,
                    timeout: 45s
                )
                print("گزارش تحلیل:")
            } catch (GuardrailViolationError err) {
                print("خطا در قوانین اخلاقی/تولید متن:", err.details)
            } catch (TimeoutError) {
                print("تحلیل بیش از حد زمان‌بر شد.")
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("خطا در قوانین اخلاقی/تولید متن:", output);
        Assert.DoesNotContain("گزارش تحلیل:", output);
    }

    [Fact]
    public async Task User_Example_4_Timeout_Branch()
    {
        string code = """
            agent FinancialAnalyst {
                model: "claude-3-5-sonnet"
                temperature: 0.1

                guardrails {
                    forbid: ["سیگنال خرید قطعی", "تضمین سود"]
                }
            }

            task analyze_apple = "صورت‌های مالی سه ماهه اخیر اپل را بررسی کن"

            try {
                report = FinancialAnalyst.achieve(
                    goal: analyze_apple,
                    timeout: 10ms
                )
                print("گزارش تحلیل:")
            } catch (GuardrailViolationError err) {
                print("خطا در قوانین اخلاقی/تولید متن:", err.details)
            } catch (TimeoutError) {
                print("تحلیل بیش از حد زمان‌بر شد.")
            }
            """;

        var output = await RunCodeAsync(code);
        Assert.Contains("تحلیل بیش از حد زمان‌بر شد.", output);
        Assert.DoesNotContain("گزارش تحلیل:", output);
    }
}
