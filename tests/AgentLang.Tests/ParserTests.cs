using AgentLang.AST;
using AgentLang.Parser;
using Xunit;

namespace AgentLang.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesMainWithAgents()
    {
        string source = """
            main {
                agent Researcher
                agent Reviewer
            }
            """;
        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.NotNull(program.Main);
        Assert.Equal(2, program.Main.Statements.Count);

        var first = Assert.IsType<AgentInvocationNode>(program.Main.Statements[0]);
        Assert.Equal("Researcher", first.AgentName);

        var second = Assert.IsType<AgentInvocationNode>(program.Main.Statements[1]);
        Assert.Equal("Reviewer", second.AgentName);
    }

    [Fact]
    public void ParsesAgentWithConfigAndTask()
    {
        string source = """
            permission ResearchPermission {
                allow browser.search
                ask filesystem.write
                cannot terminal
            }

            agent Researcher (
                model = GPT,
                permission = ResearchPermission,
                tools = [browser, filesystem]
            ) {
                context question = "What is AgentLang?"

                task research {
                    data = research(question)
                    answer = think(data)
                    return answer
                }

                print(research.result)
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var errors = string.Join("\n", parser.Diagnostics.Diagnostics.Select(d => sourceText.FormatDiagnostic(d)));
        Assert.True(!parser.Diagnostics.HasErrors, errors);
        Assert.Equal(2, program.Declarations.Count);

        var perm = Assert.IsType<PermissionDeclarationNode>(program.Declarations[0]);
        Assert.Equal("ResearchPermission", perm.Name);
        Assert.Equal(3, perm.Rules.Count);
        Assert.Equal(PermissionAction.Allow, perm.Rules[0].Action);
        Assert.Equal("browser.search", perm.Rules[0].CapabilityPattern);
        Assert.Equal(PermissionAction.Ask, perm.Rules[1].Action);
        Assert.Equal("filesystem.write", perm.Rules[1].CapabilityPattern);
        Assert.Equal(PermissionAction.Cannot, perm.Rules[2].Action);
        Assert.Equal("terminal", perm.Rules[2].CapabilityPattern);

        var agent = Assert.IsType<AgentDeclarationNode>(program.Declarations[1]);
        Assert.Equal("Researcher", agent.Name);
        Assert.Equal(3, agent.Config.Count);
        Assert.Equal("model", agent.Config[0].Key);
        Assert.Equal("permission", agent.Config[1].Key);
        Assert.Equal("tools", agent.Config[2].Key);

        Assert.Equal(3, agent.Body.Count);
        var ctx = Assert.IsType<ContextDeclarationNode>(agent.Body[0]);
        Assert.Equal("question", ctx.Name);

        var task = Assert.IsType<TaskDeclarationNode>(agent.Body[1]);
        Assert.Equal("research", task.Name);
        Assert.Equal(3, task.Body.Count);

        var stmt1 = Assert.IsType<VariableAssignmentNode>(task.Body[0]);
        Assert.Equal("data", stmt1.VariableName);
        var aiOp1 = Assert.IsType<AiOperationExpressionNode>(stmt1.Value);
        Assert.Equal("research", aiOp1.OperationName);

        var stmt2 = Assert.IsType<VariableAssignmentNode>(task.Body[1]);
        Assert.Equal("answer", stmt2.VariableName);
        var aiOp2 = Assert.IsType<AiOperationExpressionNode>(stmt2.Value);
        Assert.Equal("think", aiOp2.OperationName);

        var ret = Assert.IsType<ReturnStatementNode>(task.Body[2]);
        Assert.NotNull(ret.Value);

        var printStmt = Assert.IsType<ExpressionStatementNode>(agent.Body[2]);
        var call = Assert.IsType<CallExpressionNode>(printStmt.Expression);
        var memberAccess = Assert.IsType<MemberAccessExpressionNode>(call.Arguments[0]);
        Assert.Equal("result", memberAccess.MemberName);
    }

    [Fact]
    public void ParsesMultiAgentWithParallelBlock()
    {
        string source = """
            multiagent ResearchTeam {
                parallel {
                    agent ResearcherA (model = GPT) {
                        task run { return "A" }
                    }
                    agent ResearcherB (model = Claude) {
                        task run { return "B" }
                    }
                }
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        var multi = Assert.IsType<MultiAgentDeclarationNode>(program.Declarations[0]);
        Assert.Equal("ResearchTeam", multi.Name);
        Assert.Single(multi.Body);

        var parallel = Assert.IsType<ParallelBlockNode>(multi.Body[0]);
        Assert.Equal(2, parallel.Body.Count);
        Assert.IsType<AgentDeclarationNode>(parallel.Body[0]);
        Assert.IsType<AgentDeclarationNode>(parallel.Body[1]);
    }

    [Fact]
    public void ParsesEventsAndRetryAndTryCatch()
    {
        string source = """
            event research.finished {
                agent Reviewer
            }

            main {
                try {
                    retry 3 {
                        data = research("AI")
                    }
                } catch err {
                    print("Error: " + err)
                }
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var errors = string.Join("\n", parser.Diagnostics.Diagnostics.Select(d => sourceText.FormatDiagnostic(d)));
        Assert.True(!parser.Diagnostics.HasErrors, errors);
        var evt = Assert.IsType<EventDeclarationNode>(program.Declarations[0]);
        Assert.Equal("research.finished", evt.Target);
        Assert.Single(evt.Body);

        Assert.NotNull(program.Main);
        var tryCatch = Assert.IsType<TryCatchStatementNode>(program.Main.Statements[0]);
        Assert.Equal("err", tryCatch.CatchVariable);
        Assert.Single(tryCatch.TryBody);
        var retry = Assert.IsType<RetryStatementNode>(tryCatch.TryBody[0]);
        Assert.Single(retry.Body);
    }

    [Fact]
    public void ParsesControlFlow()
    {
        string source = """
            main {
                if active == true {
                    repeat 5 {
                        count = count + 1
                    }
                } else {
                    while count > 0 {
                        count = count - 1
                    }
                }
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.NotNull(program.Main);
        var ifStmt = Assert.IsType<IfStatementNode>(program.Main.Statements[0]);
        Assert.Single(ifStmt.ThenBranch);
        Assert.NotNull(ifStmt.ElseBranch);
        Assert.Single(ifStmt.ElseBranch);

        Assert.IsType<RepeatStatementNode>(ifStmt.ThenBranch[0]);
        Assert.IsType<WhileStatementNode>(ifStmt.ElseBranch[0]);
    }

    [Fact]
    public void ParsesImportApiDeclarations()
    {
        string source = """
            importapi gemini = "AIzaSyFakeKey", model = "gemini-1.5-flash"
            importapi tavily (key = "tvly-test", max = 5)

            main {
                print("apis imported")
            }
            """;

        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        Assert.False(parser.Diagnostics.HasErrors);
        Assert.Equal(2, program.Declarations.Count);

        var first = Assert.IsType<ImportApiDeclarationNode>(program.Declarations[0]);
        Assert.Equal("gemini", first.Provider);
        Assert.True(first.Options.ContainsKey("model"));

        var second = Assert.IsType<ImportApiDeclarationNode>(program.Declarations[1]);
        Assert.Equal("tavily", second.Provider);
        Assert.True(second.Options.ContainsKey("key"));
    }
}
