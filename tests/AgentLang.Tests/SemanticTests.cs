using AgentLang.AST;
using AgentLang.Parser;
using AgentLang.Semantic;
using Xunit;

namespace AgentLang.Tests;

public class SemanticTests
{
    [Fact]
    public void DetectsDuplicateAgentDeclaration()
    {
        string source = """
            agent Researcher { }
            agent Researcher { }
            """;
        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        Assert.True(analyzer.Diagnostics.HasErrors);
        var err = analyzer.Diagnostics.Diagnostics.First(d => d.Id == "AL2003");
        Assert.Contains("Duplicate agent declaration 'Researcher'", err.Message);
    }

    [Fact]
    public void DetectsUnknownAgentWithSuggestion()
    {
        string source = """
            agent Researcher { }

            main {
                agent Researchr
            }
            """;
        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        Assert.True(analyzer.Diagnostics.HasErrors);
        var err = analyzer.Diagnostics.Diagnostics.First(d => d.Id == "AL2009");
        Assert.Contains("Unknown agent 'Researchr'", err.Message);
        Assert.NotNull(err.Suggestion);
        Assert.Contains("Researcher", err.Suggestion);
    }

    [Fact]
    public void DetectsUnknownToolWithSuggestion()
    {
        string source = """
            agent Researcher (
                tools = [brower]
            ) { }
            """;
        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        Assert.True(analyzer.Diagnostics.HasErrors);
        var err = analyzer.Diagnostics.Diagnostics.First(d => d.Id == "AL2006");
        Assert.Contains("Unknown tool 'brower'", err.Message);
        Assert.NotNull(err.Suggestion);
        Assert.Contains("browser", err.Suggestion);
    }

    [Fact]
    public void ValidProgramPassesAnalysis()
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
                context question = "test"

                task research {
                    data = research(question)
                    return data
                }
            }

            main {
                agent Researcher
            }
            """;
        var sourceText = new SourceText(source);
        var parser = new Parser.Parser(sourceText);
        var program = parser.ParseProgram();

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);

        Assert.False(analyzer.Diagnostics.HasErrors);
    }
}
